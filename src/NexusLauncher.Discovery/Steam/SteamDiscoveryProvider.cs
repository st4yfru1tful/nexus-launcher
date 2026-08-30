using System.Globalization;
using NexusLauncher.Core.Discovery;
using NexusLauncher.Core.Domain;
using NexusLauncher.Core.Paths;
using NexusLauncher.Discovery.Abstractions;
using System.Runtime.Versioning;

namespace NexusLauncher.Discovery.Steam;

/// <summary>
/// Discovers installed Steam titles from local Steam manifests and library folder
/// records.  It never scrapes or automates the Steam client UI.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SteamDiscoveryProvider : IInstallationDiscoveryProvider
{
    private readonly ISteamInstallationLocator _installationLocator;
    private readonly IDiscoveryFileSystem _fileSystem;
    private readonly ISteamExecutableLocator _executableLocator;

    public SteamDiscoveryProvider(
        ISteamInstallationLocator? installationLocator = null,
        IDiscoveryFileSystem? fileSystem = null,
        ISteamExecutableLocator? executableLocator = null)
    {
        _fileSystem = fileSystem ?? new PhysicalDiscoveryFileSystem();
        _installationLocator = installationLocator ?? new SteamInstallationLocator(fileSystem: _fileSystem);
        _executableLocator = executableLocator ?? new SteamExecutableLocator(_fileSystem);
    }

    public string Id => "steam";

    public Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<DiscoveredInstallation>();
        var issues = new List<DiscoveryIssue>();
        foreach (var installation in _installationLocator.FindInstallations())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var library in GetLibraries(installation, issues))
            {
                DiscoverLibrary(library, items, issues, cancellationToken);
            }
        }

        return Task.FromResult(new DiscoveryResult
        {
            ProviderId = Id,
            Items = items
                .GroupBy(
                    item => item.Identities.Count > 0 ? item.Identities[0].CanonicalKey : item.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray(),
            Issues = issues,
        });
    }

    private IReadOnlyList<SteamLibraryFolder> GetLibraries(SteamInstallation installation, List<DiscoveryIssue> issues)
    {
        var libraries = new List<SteamLibraryFolder>
        {
            new() { Index = 0, Path = installation.RootPath },
        };
        var foldersPath = Path.Combine(installation.RootPath, "steamapps", "libraryfolders.vdf");
        if (!_fileSystem.FileExists(foldersPath))
        {
            return libraries;
        }

        try
        {
            libraries.AddRange(SteamLibraryFoldersParser.Parse(_fileSystem.ReadAllText(foldersPath)));
        }
        catch (IOException exception)
        {
            issues.Add(new DiscoveryIssue(Id, $"Could not read Steam library folders: {exception.Message}", IsTransient: true));
        }
        catch (Steam.Vdf.VdfParseException exception)
        {
            issues.Add(new DiscoveryIssue(Id, $"Could not parse Steam library folders: {exception.Message}"));
        }

        return libraries
            .Where(library => _fileSystem.DirectoryExists(library.Path))
            .DistinctBy(library => library.Path, PathNormalizer.Comparer)
            .ToArray();
    }

    private void DiscoverLibrary(
        SteamLibraryFolder library,
        List<DiscoveredInstallation> items,
        List<DiscoveryIssue> issues,
        CancellationToken cancellationToken)
    {
        var steamAppsPath = Path.Combine(library.Path, "steamapps");
        if (!_fileSystem.DirectoryExists(steamAppsPath))
        {
            return;
        }

        IEnumerable<string> manifests;
        try
        {
            manifests = _fileSystem.EnumerateFiles(steamAppsPath, "appmanifest_*.acf", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (IOException exception)
        {
            issues.Add(new DiscoveryIssue(Id, $"Could not enumerate Steam manifests in '{steamAppsPath}': {exception.Message}", IsTransient: true));
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            issues.Add(new DiscoveryIssue(Id, $"Steam manifest folder is inaccessible: {exception.Message}"));
            return;
        }

        foreach (var manifestPath in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SteamAppManifest manifest;
            try
            {
                manifest = SteamAppManifestParser.Parse(_fileSystem.ReadAllText(manifestPath));
            }
            catch (IOException exception)
            {
                issues.Add(new DiscoveryIssue(Id, $"Could not read Steam appmanifest '{manifestPath}': {exception.Message}", IsTransient: true));
                continue;
            }
            catch (Steam.Vdf.VdfParseException exception)
            {
                issues.Add(new DiscoveryIssue(Id, $"Skipped malformed Steam appmanifest '{manifestPath}': {exception.Message}"));
                continue;
            }

            if (!manifest.IsInstalled)
            {
                continue;
            }

            var installPath = Path.Combine(steamAppsPath, "common", manifest.InstallDirectory);
            if (!_fileSystem.DirectoryExists(installPath))
            {
                issues.Add(new DiscoveryIssue(Id, $"Steam reports '{manifest.Name}' as installed, but its directory is unavailable."));
                continue;
            }

            var executablePath = _executableLocator.FindBestExecutable(installPath, manifest.Name, cancellationToken);
            items.Add(new DiscoveredInstallation
            {
                DisplayName = manifest.Name,
                Category = LibraryItemCategory.Game,
                InstallPath = installPath,
                Version = manifest.BuildId,
                ProviderId = Id,
                Launch = new LaunchCommand
                {
                    ExecutablePath = executablePath,
                    WorkingDirectory = executablePath is null ? null : installPath,
                    LaunchUri = $"steam://run/{manifest.AppId.ToString(CultureInfo.InvariantCulture)}",
                },
                Identities = new[] { new ProviderIdentity("steam", "appId", manifest.AppId.ToString(CultureInfo.InvariantCulture)) },
                SourcePaths = new[] { manifestPath },
            });
        }
    }
}
