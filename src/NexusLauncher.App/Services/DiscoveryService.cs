using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NexusLauncher.Core.Deduplication;
using NexusLauncher.Core.Discovery;
using NexusLauncher.Core.Domain;
using NexusLauncher.Discovery;
using NexusLauncher.Discovery.Steam;
using NexusLauncher.Discovery.Windows;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class DiscoveryService(ExecutableInspector inspector)
{
    private readonly ExecutableInspector _inspector = inspector;

    public async Task<IReadOnlyList<LibraryItem>> DiscoverAsync(AppSettings settings, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var candidates = new List<LibraryItem>();
        progress?.Report("Checking Steam, installed applications, and Start menu shortcuts…");
        candidates.AddRange(await DiscoverThroughProvidersAsync(settings, cancellationToken));

        foreach (var folder in settings.ScanFolders.Where(Directory.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Checking {folder}…");
            candidates.AddRange(await Task.Run(() => DiscoverFolder(folder, cancellationToken).ToArray(), cancellationToken));
        }

        return Deduplicate(candidates, settings.IgnoredPaths);
    }

    private static async Task<IReadOnlyList<LibraryItem>> DiscoverThroughProvidersAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var providers = new List<IInstallationDiscoveryProvider> { new SteamDiscoveryProvider() };
        if (settings.IncludeInstalledApplications)
        {
            providers.Add(new RegistryInstalledApplicationsDiscoveryProvider());
        }

        if (settings.IncludeStartMenuShortcuts)
        {
            providers.Add(new StartMenuDiscoveryProvider());
        }

        var coordinator = new DiscoveryCoordinator(providers);
        var scan = await Task.Run(
            async () => await coordinator.DiscoverAsync(new DiscoveryRequest { IsQuickScan = true }, cancellationToken),
            cancellationToken);
        var deduplicated = new LibraryItemDuplicateDetector().Deduplicate(scan.Items).UniqueItems;
        return deduplicated.Select(ToLibraryItem).ToArray();
    }

    private static LibraryItem ToLibraryItem(DiscoveredInstallation installation)
    {
        return new LibraryItem
        {
            Name = installation.DisplayName,
            Category = installation.Category switch
            {
                LibraryItemCategory.Game => LibraryCategory.Game,
                LibraryItemCategory.Application => LibraryCategory.Application,
                LibraryItemCategory.Utility => LibraryCategory.Utility,
                LibraryItemCategory.DevelopmentTool => LibraryCategory.DevelopmentTool,
                LibraryItemCategory.MediaSoftware => LibraryCategory.MediaSoftware,
                LibraryItemCategory.Launcher => LibraryCategory.Launcher,
                _ => LibraryCategory.Unknown
            },
            ExecutablePath = installation.Launch.ExecutablePath,
            LaunchUri = installation.Launch.LaunchUri,
            LaunchArguments = installation.Launch.Arguments,
            WorkingDirectory = installation.Launch.WorkingDirectory,
            InstallPath = installation.InstallPath,
            Provider = installation.ProviderId switch
            {
                "steam" => "Steam",
                "windows-registry" => "Windows",
                "start-menu" => "Start Menu",
                _ => installation.ProviderId
            },
            Publisher = installation.Publisher,
            Version = installation.Version,
            DateDiscovered = installation.DiscoveredAt,
            Tags = installation.Identities
                .Where(identity => identity.IsUsable)
                .Select(identity => $"{identity.Provider}:{identity.Key}")
                .ToList()
        };
    }

    private IEnumerable<LibraryItem> DiscoverInstalledApplications(CancellationToken cancellationToken)
    {
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        var roots = new[] { Registry.LocalMachine, Registry.CurrentUser };
        foreach (var root in roots)
        {
            foreach (var path in paths)
            {
                using var uninstall = root.OpenSubKey(path);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry is null)
                    {
                        continue;
                    }

                    var displayName = entry.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName) || IsIgnoredDisplayName(displayName))
                    {
                        continue;
                    }

                    var installPath = entry.GetValue("InstallLocation") as string;
                    var iconPath = ExtractExecutablePath(entry.GetValue("DisplayIcon") as string);
                    var executable = FindExecutable(iconPath, installPath);
                    yield return new LibraryItem
                    {
                        Name = displayName.Trim(),
                        Category = _inspector.Classify(displayName, entry.GetValue("Publisher") as string, installPath),
                        ExecutablePath = executable,
                        InstallPath = Directory.Exists(installPath) ? installPath : Path.GetDirectoryName(executable),
                        WorkingDirectory = Path.GetDirectoryName(executable),
                        IconPath = iconPath,
                        Version = entry.GetValue("DisplayVersion") as string,
                        Publisher = entry.GetValue("Publisher") as string,
                        Provider = "Windows",
                        Description = entry.GetValue("Comments") as string
                    };
                }
            }
        }
    }

    private IEnumerable<LibraryItem> DiscoverSteam(CancellationToken cancellationToken)
    {
        var steamPath = GetSteamPath();
        if (steamPath is null)
        {
            yield break;
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
        var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            foreach (var match in Regex.Matches(File.ReadAllText(libraryFile), "\\\"path\\\"\\s+\\\"(?<path>(?:\\\\\\\"|[^\\\"])*)\\\"", RegexOptions.IgnoreCase).Cast<Match>())
            {
                var value = match.Groups["path"].Value.Replace("\\\\", "\\");
                if (Directory.Exists(value))
                {
                    libraries.Add(value);
                }
            }
        }

        foreach (var library in libraries)
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = File.ReadAllText(manifestPath);
                var id = ExtractVdfValue(manifest, "appid");
                var name = ExtractVdfValue(manifest, "name");
                var installDirectory = ExtractVdfValue(manifest, "installdir");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var folder = string.IsNullOrWhiteSpace(installDirectory) ? null : Path.Combine(steamApps, "common", installDirectory);
                var exe = FindGameExecutable(folder);
                yield return new LibraryItem
                {
                    Name = name,
                    Category = LibraryCategory.Game,
                    Provider = "Steam",
                    LaunchUri = $"steam://rungameid/{id}",
                    ExecutablePath = exe,
                    WorkingDirectory = folder,
                    InstallPath = folder,
                    Description = $"Steam App ID {id}",
                    Tags = ["Steam"]
                };
            }
        }
    }

    private IEnumerable<LibraryItem> DiscoverStartMenu(CancellationToken cancellationToken)
    {
        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            foreach (var shortcut in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolveShortcut(shortcut);
                if (target is null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !_inspector.IsLikelyLaunchable(target))
                {
                    continue;
                }

                var item = _inspector.CreateFromExecutable(target);
                item.Name = Path.GetFileNameWithoutExtension(shortcut);
                item.Provider = "Start Menu";
                yield return item;
            }
        }
    }

    private IEnumerable<LibraryItem> DiscoverFolder(string folder, CancellationToken cancellationToken)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateDirectories(folder).SelectMany(directory => SafeEnumerateExecutables(directory)));
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var executable in files.Take(250))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_inspector.IsLikelyLaunchable(executable))
            {
                yield return _inspector.CreateFromExecutable(executable);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateExecutables(string directory)
    {
        try { return Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private static IReadOnlyList<LibraryItem> Deduplicate(IEnumerable<LibraryItem> candidates, IEnumerable<string> ignoredPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(item => !ignoredPaths.Any(path => !string.IsNullOrWhiteSpace(path) && (item.ExecutablePath?.StartsWith(path, StringComparison.OrdinalIgnoreCase) == true || item.InstallPath?.StartsWith(path, StringComparison.OrdinalIgnoreCase) == true)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => seen.Add(IdentityFor(item)))
            .OrderBy(item => item.Name)
            .ToList();
    }

    private static string IdentityFor(LibraryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.LaunchUri)) return item.LaunchUri;
        if (!string.IsNullOrWhiteSpace(item.ExecutablePath)) return Path.GetFullPath(item.ExecutablePath).TrimEnd('\\').ToUpperInvariant();
        return $"{item.Name.Trim().ToUpperInvariant()}|{item.InstallPath?.Trim().ToUpperInvariant()}";
    }

    private static string? GetSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var registryValue = key?.GetValue("SteamPath") as string;
        var candidates = new[]
        {
            registryValue,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
    }

    private static string? ExtractVdfValue(string content, string key)
    {
        var match = Regex.Match(content, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<value>(?:\\\\\\\"|[^\\\"])*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Replace("\\\\", "\\") : null;
    }

    private static string? ExtractExecutablePath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var trimmed = displayIcon.Trim().Trim('"');
        var comma = trimmed.LastIndexOf(',');
        if (comma >= 0) trimmed = trimmed[..comma];
        return File.Exists(trimmed) && trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed : null;
    }

    private string? FindExecutable(string? iconPath, string? installPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && _inspector.IsLikelyLaunchable(iconPath)) return iconPath;
        return FindGameExecutable(installPath);
    }

    private string? FindGameExecutable(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        try
        {
            return Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(_inspector.IsLikelyLaunchable)
                .OrderBy(path => Path.GetFileName(path).Contains("launcher", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path.Length)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static bool IsIgnoredDisplayName(string value)
    {
        var text = value.ToLowerInvariant();
        return text.Contains("visual c++") || text.Contains(".net") || text.Contains("update for") || text.Contains("runtime") || text.Contains("redistributable") || text.Contains("driver");
    }

    private static string? ResolveShortcut(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            var shell = Activator.CreateInstance(shellType);
            var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            return shortcut?.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
        }
        catch
        {
            return null;
        }
    }
}
