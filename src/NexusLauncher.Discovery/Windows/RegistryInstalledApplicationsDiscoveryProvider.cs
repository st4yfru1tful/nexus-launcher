using Microsoft.Win32;
using NexusLauncher.Core.Classification;
using NexusLauncher.Core.Discovery;
using NexusLauncher.Core.Domain;
using NexusLauncher.Core.Paths;
using NexusLauncher.Discovery.Abstractions;
using NexusLauncher.Discovery.Metadata;
using System.Runtime.Versioning;

namespace NexusLauncher.Discovery.Windows;

/// <summary>
/// Discovers user-launchable installed applications through the standard Windows
/// uninstall registry records.  It intentionally does not use an UninstallString
/// as a launch target.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryInstalledApplicationsDiscoveryProvider : IInstallationDiscoveryProvider
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly RegistryLocation[] Locations =
    {
        new(RegistryHive.LocalMachine, RegistryView.Registry64),
        new(RegistryHive.LocalMachine, RegistryView.Registry32),
        new(RegistryHive.CurrentUser, RegistryView.Registry64),
    };

    private readonly IRegistryAccessor _registry;
    private readonly IDiscoveryFileSystem _fileSystem;
    private readonly IExecutableMetadataReader _metadataReader;
    private readonly IExecutableClassifier _classifier;

    public RegistryInstalledApplicationsDiscoveryProvider(
        IRegistryAccessor? registry = null,
        IDiscoveryFileSystem? fileSystem = null,
        IExecutableMetadataReader? metadataReader = null,
        IExecutableClassifier? classifier = null)
    {
        _registry = registry ?? new WindowsRegistryAccessor();
        _fileSystem = fileSystem ?? new PhysicalDiscoveryFileSystem();
        _metadataReader = metadataReader ?? new FileVersionExecutableMetadataReader();
        _classifier = classifier ?? new ExecutableClassifier();
    }

    public string Id => "windows-registry";

    public Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<DiscoveredInstallation>();
        foreach (var location in Locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in _registry.EnumerateSubKeys(location.Hive, location.View, UninstallPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = ToInstallation(location, entry);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return Task.FromResult(new DiscoveryResult
        {
            ProviderId = Id,
            Items = items,
        });
    }

    private DiscoveredInstallation? ToInstallation(RegistryLocation location, RegistryKeySnapshot entry)
    {
        var values = entry.Values;
        var displayName = values.GetString("DisplayName")?.Trim();
        if (string.IsNullOrWhiteSpace(displayName) ||
            values.IsEnabled("SystemComponent") ||
            IsUpdateRecord(values))
        {
            return null;
        }

        var executablePath = ExtractExecutablePath(values.GetString("DisplayIcon"));
        if (executablePath is null || !_fileSystem.FileExists(executablePath))
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
            Publisher = values.GetString("Publisher"),
            ParentDirectoryName = Path.GetFileName(Path.GetDirectoryName(executablePath)),
            ProviderId = Id,
            IsRegisteredInstallation = true,
        });

        if (!classification.ShouldInclude)
        {
            return null;
        }

        var rawInstallPath = values.GetString("InstallLocation");
        var installPath = PathNormalizer.TryNormalize(rawInstallPath, out var normalizedInstallPath)
            ? normalizedInstallPath
            : Path.GetDirectoryName(executablePath);
        var registryPath = $"{location.Hive}\\{UninstallPath}\\{entry.Name}";

        return new DiscoveredInstallation
        {
            DisplayName = displayName,
            Category = classification.Category == LibraryItemCategory.Unknown
                ? LibraryItemCategory.Application
                : classification.Category,
            InstallPath = installPath,
            IconPath = IconPathNormalizer.TryNormalize(executablePath, out var iconPath) ? iconPath : null,
            Publisher = values.GetString("Publisher"),
            Version = values.GetString("DisplayVersion") ?? metadata.FileVersion,
            ProviderId = Id,
            Launch = new LaunchCommand
            {
                ExecutablePath = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
            },
            Identities = new[]
            {
                new ProviderIdentity(Id, "uninstallKey", $"{location.Hive}:{location.View}:{entry.Name}"),
            },
            SourcePaths = new[] { registryPath },
        };
    }

    private static bool IsUpdateRecord(IReadOnlyDictionary<string, object?> values)
    {
        var releaseType = values.GetString("ReleaseType");
        var parentKeyName = values.GetString("ParentKeyName");
        return !string.IsNullOrWhiteSpace(parentKeyName) ||
               (!string.IsNullOrWhiteSpace(releaseType) &&
                (releaseType.Contains("update", StringComparison.OrdinalIgnoreCase) ||
                 releaseType.Contains("hotfix", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? ExtractExecutablePath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var candidate = displayIcon.Trim();
        if (candidate.StartsWith('"'))
        {
            var closingQuoteOffset = candidate.AsSpan(1).IndexOf('"');
            var closingQuote = closingQuoteOffset < 0 ? -1 : closingQuoteOffset + 1;
            candidate = closingQuote > 0 ? candidate[1..closingQuote] : candidate[1..];
        }
        else
        {
            var comma = candidate.IndexOf(',', StringComparison.Ordinal);
            if (comma >= 0)
            {
                candidate = candidate[..comma];
            }
        }

        return candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
               PathNormalizer.TryNormalize(candidate, out var normalized)
            ? normalized
            : null;
    }

    private sealed record RegistryLocation(RegistryHive Hive, RegistryView View);
}
