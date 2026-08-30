using NexusLauncher.Core.Classification;
using NexusLauncher.Discovery.Abstractions;
using NexusLauncher.Discovery.Metadata;

namespace NexusLauncher.Discovery.Steam;

/// <summary>Finds the most plausible player-facing executable in a Steam install.</summary>
public interface ISteamExecutableLocator
{
    string? FindBestExecutable(string installPath, string displayName, CancellationToken cancellationToken);
}

/// <summary>
/// Bounded executable picker for Steam game directories.  Steam appmanifests do
/// not contain a canonical EXE, so this favors user-facing files while letting the
/// Steam URI remain a reliable fallback.
/// </summary>
public sealed class SteamExecutableLocator : ISteamExecutableLocator
{
    private readonly IDiscoveryFileSystem _fileSystem;
    private readonly IExecutableMetadataReader _metadataReader;
    private readonly IExecutableClassifier _classifier;

    public SteamExecutableLocator(
        IDiscoveryFileSystem? fileSystem = null,
        IExecutableMetadataReader? metadataReader = null,
        IExecutableClassifier? classifier = null)
    {
        _fileSystem = fileSystem ?? new PhysicalDiscoveryFileSystem();
        _metadataReader = metadataReader ?? new FileVersionExecutableMetadataReader();
        _classifier = classifier ?? new ExecutableClassifier();
    }

    /// <summary>Maximum recursive executable candidates inspected for one game.</summary>
    public int MaximumCandidates { get; init; } = 250;

    public string? FindBestExecutable(string installPath, string displayName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!_fileSystem.DirectoryExists(installPath))
        {
            return null;
        }

        var gameName = Canonicalize(displayName);
        var candidates = new List<(string Path, int Score)>();
        try
        {
            foreach (var path in _fileSystem.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories).Take(MaximumCandidates))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = _metadataReader.Read(path);
                var classification = _classifier.Classify(new ExecutableClassificationInput
                {
                    FilePath = path,
                    FileDescription = metadata.FileDescription,
                    ProductName = metadata.ProductName,
                    CompanyName = metadata.CompanyName,
                    FileVersion = metadata.FileVersion,
                    ParentDirectoryName = Path.GetFileName(Path.GetDirectoryName(path)),
                    ProviderId = "steam",
                    IsFromLauncherManifest = true,
                    HasGameEngineEvidence = HasGameEngineEvidence(path),
                });

                if (!classification.ShouldInclude)
                {
                    continue;
                }

                candidates.Add((path, Score(path, installPath, gameName, classification.Confidence)));
            }
        }
        catch (IOException)
        {
            // The Steam URI fallback remains usable if a partially removed game cannot be enumerated.
        }
        catch (UnauthorizedAccessException)
        {
            // Some game folders may be unavailable to the current user.
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private bool HasGameEngineEvidence(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        var fileName = Path.GetFileNameWithoutExtension(executablePath);
        return directory is not null &&
               (_fileSystem.DirectoryExists(Path.Combine(directory, fileName + "_Data")) ||
                _fileSystem.FileExists(Path.Combine(directory, "UnityPlayer.dll")) ||
                _fileSystem.FileExists(Path.Combine(directory, "GameAssembly.dll")));
    }

    private static int Score(string executablePath, string installPath, string canonicalDisplayName, double classificationConfidence)
    {
        var score = (int)Math.Round(classificationConfidence * 100d, MidpointRounding.AwayFromZero);
        var executableName = Canonicalize(Path.GetFileNameWithoutExtension(executablePath));
        var installDirectoryName = Canonicalize(Path.GetFileName(installPath));
        if (executableName == canonicalDisplayName)
        {
            score += 40;
        }
        else if (executableName.Contains(canonicalDisplayName, StringComparison.Ordinal) ||
                 canonicalDisplayName.Contains(executableName, StringComparison.Ordinal))
        {
            score += 20;
        }

        if (executableName == installDirectoryName)
        {
            score += 15;
        }

        var relativePath = Path.GetRelativePath(installPath, executablePath);
        if (!relativePath.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            score += 20;
        }

        return score;
    }

    private static string Canonicalize(string? value)
    {
        return string.Concat((value ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToUpperInvariant));
    }
}
