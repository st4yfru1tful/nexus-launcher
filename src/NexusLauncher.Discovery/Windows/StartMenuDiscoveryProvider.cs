using NexusLauncher.Core.Classification;
using NexusLauncher.Core.Discovery;
using NexusLauncher.Core.Domain;
using NexusLauncher.Core.Paths;
using NexusLauncher.Discovery.Abstractions;
using NexusLauncher.Discovery.Metadata;
using System.Runtime.Versioning;

namespace NexusLauncher.Discovery.Windows;

/// <summary>Discovers launchable .lnk targets exposed through the Windows Start menu.</summary>
[SupportedOSPlatform("windows")]
public sealed class StartMenuDiscoveryProvider : IInstallationDiscoveryProvider
{
    private readonly IDiscoveryFileSystem _fileSystem;
    private readonly IShortcutResolver _shortcutResolver;
    private readonly IExecutableMetadataReader _metadataReader;
    private readonly IExecutableClassifier _classifier;
    private readonly IReadOnlyList<string> _locations;

    public StartMenuDiscoveryProvider(
        IDiscoveryFileSystem? fileSystem = null,
        IShortcutResolver? shortcutResolver = null,
        IExecutableMetadataReader? metadataReader = null,
        IExecutableClassifier? classifier = null,
        IEnumerable<string>? locations = null)
    {
        _fileSystem = fileSystem ?? new PhysicalDiscoveryFileSystem();
        _shortcutResolver = shortcutResolver ?? new ShellLinkShortcutResolver();
        _metadataReader = metadataReader ?? new FileVersionExecutableMetadataReader();
        _classifier = classifier ?? new ExecutableClassifier();
        _locations = locations?.ToArray() ?? GetDefaultLocations().ToArray();
    }

    public string Id => "start-menu";

    public Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<DiscoveredInstallation>();
        var issues = new List<DiscoveryIssue>();
        foreach (var location in _locations.Distinct(PathNormalizer.Comparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_fileSystem.DirectoryExists(location))
            {
                continue;
            }

            try
            {
                foreach (var shortcutPath in _fileSystem.EnumerateFiles(location, "*.lnk", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = ToInstallation(shortcutPath);
                    if (item is not null)
                    {
                        items.Add(item);
                    }
                }
            }
            catch (IOException exception)
            {
                issues.Add(new DiscoveryIssue(Id, $"Could not enumerate Start-menu shortcuts in '{location}': {exception.Message}", IsTransient: true));
            }
            catch (UnauthorizedAccessException exception)
            {
                issues.Add(new DiscoveryIssue(Id, $"Start-menu folder is inaccessible: {exception.Message}"));
            }
        }

        return Task.FromResult(new DiscoveryResult
        {
            ProviderId = Id,
            Items = items,
            Issues = issues,
        });
    }

    private DiscoveredInstallation? ToInstallation(string shortcutPath)
    {
        if (!_shortcutResolver.TryResolve(shortcutPath, out var shortcut) ||
            !PathNormalizer.TryNormalize(shortcut.TargetPath, out var executablePath) ||
            !executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !_fileSystem.FileExists(executablePath))
        {
            return null;
        }

        var metadata = _metadataReader.Read(executablePath);
        var classification = _classifier.Classify(new ExecutableClassificationInput
        {
            FilePath = executablePath,
            FileDescription = metadata.FileDescription,
            ProductName = metadata.ProductName,
            CompanyName = metadata.CompanyName,
            FileVersion = metadata.FileVersion,
            ParentDirectoryName = Path.GetFileName(Path.GetDirectoryName(executablePath)),
            ProviderId = Id,
            IsStartMenuTarget = true,
        });
        if (!classification.ShouldInclude)
        {
            return null;
        }

        var name = metadata.ProductName ?? Path.GetFileNameWithoutExtension(shortcutPath);
        return new DiscoveredInstallation
        {
            DisplayName = name,
            Category = classification.Category == LibraryItemCategory.Unknown
                ? LibraryItemCategory.Application
                : classification.Category,
            InstallPath = Path.GetDirectoryName(executablePath),
            Publisher = metadata.CompanyName,
            Version = metadata.FileVersion,
            ProviderId = Id,
            Launch = new LaunchCommand
            {
                ExecutablePath = executablePath,
                Arguments = shortcut.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)
                    ? Path.GetDirectoryName(executablePath)
                    : shortcut.WorkingDirectory,
            },
            Identities = new[]
            {
                new ProviderIdentity(Id, "shortcutPath", NormalizeShortcutIdentity(shortcutPath)),
            },
            SourcePaths = new[] { shortcutPath },
        };
    }

    private static IEnumerable<string> GetDefaultLocations()
    {
        var userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        if (!string.IsNullOrWhiteSpace(userStartMenu))
        {
            yield return Path.Combine(userStartMenu, "Programs");
        }

        if (!string.IsNullOrWhiteSpace(commonStartMenu))
        {
            yield return Path.Combine(commonStartMenu, "Programs");
        }
    }

    private static string NormalizeShortcutIdentity(string shortcutPath)
    {
        return PathNormalizer.TryNormalize(shortcutPath, out var normalized) ? normalized : shortcutPath.Trim();
    }
}
