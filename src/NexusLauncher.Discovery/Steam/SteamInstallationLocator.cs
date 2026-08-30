using Microsoft.Win32;
using NexusLauncher.Core.Paths;
using NexusLauncher.Discovery.Abstractions;
using System.Runtime.Versioning;

namespace NexusLauncher.Discovery.Steam;

/// <summary>A locally installed Steam client root.</summary>
public sealed record SteamInstallation(string RootPath);

/// <summary>Finds local Steam client roots without scraping the Steam UI.</summary>
public interface ISteamInstallationLocator
{
    IReadOnlyList<SteamInstallation> FindInstallations();
}

/// <summary>
/// Locates Steam via its documented local registry values and conventional install
/// locations.  Missing or inaccessible paths are simply ignored.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SteamInstallationLocator : ISteamInstallationLocator
{
    private static readonly (RegistryHive Hive, RegistryView View, string Key, string Value)[] RegistryLocations =
    {
        (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Valve\Steam", "SteamPath"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Valve\Steam", "InstallPath"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Valve\Steam", "InstallPath"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\WOW6432Node\Valve\Steam", "InstallPath"),
    };

    private readonly IRegistryAccessor _registry;
    private readonly IDiscoveryFileSystem _fileSystem;
    private readonly IReadOnlyList<string> _additionalCandidates;

    public SteamInstallationLocator(
        IRegistryAccessor? registry = null,
        IDiscoveryFileSystem? fileSystem = null,
        IEnumerable<string>? additionalCandidates = null)
    {
        _registry = registry ?? new WindowsRegistryAccessor();
        _fileSystem = fileSystem ?? new PhysicalDiscoveryFileSystem();
        _additionalCandidates = additionalCandidates?.ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<SteamInstallation> FindInstallations()
    {
        var candidates = new List<string>();
        foreach (var location in RegistryLocations)
        {
            var value = _registry.GetValue(location.Hive, location.View, location.Key, location.Value);
            var path = RegistryValueExtensions.ToString(value);
            if (!string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(path);
            }
        }

        candidates.AddRange(_additionalCandidates);
        candidates.AddRange(GetConventionalLocations());

        return candidates
            .Select(candidate => PathNormalizer.TryNormalize(candidate, out var normalized) ? normalized : null)
            .Where(candidate => candidate is not null && _fileSystem.DirectoryExists(candidate))
            .Select(candidate => new SteamInstallation(candidate!))
            .DistinctBy(installation => installation.RootPath, PathNormalizer.Comparer)
            .ToArray();
    }

    private static IEnumerable<string> GetConventionalLocations()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Steam");
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Steam");
        }
    }
}
