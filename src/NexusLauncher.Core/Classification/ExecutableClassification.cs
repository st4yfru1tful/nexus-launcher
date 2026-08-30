using NexusLauncher.Core.Domain;
using NexusLauncher.Core.Paths;

namespace NexusLauncher.Core.Classification;

/// <summary>Metadata and provenance available to the local executable classifier.</summary>
public sealed record ExecutableClassificationInput
{
    public string FilePath { get; init; } = string.Empty;

    public string? FileName { get; init; }

    public string? FileDescription { get; init; }

    public string? ProductName { get; init; }

    public string? CompanyName { get; init; }

    public string? FileVersion { get; init; }

    public string? Publisher { get; init; }

    public string? ParentDirectoryName { get; init; }

    public string? ProviderId { get; init; }

    public LibraryItemCategory? DeclaredCategory { get; init; }

    public bool IsFromLauncherManifest { get; init; }

    public bool IsRegisteredInstallation { get; init; }

    public bool IsStartMenuTarget { get; init; }

    public bool HasGameEngineEvidence { get; init; }
}

/// <summary>The local classification result, including explainable scores.</summary>
public sealed record ExecutableClassificationResult
{
    public LibraryItemCategory Category { get; init; }

    public double Confidence { get; init; }

    /// <summary>
    /// Probability-like signal that this executable is infrastructure rather than
    /// an item that should appear in a user's library.
    /// </summary>
    public double IgnoreConfidence { get; init; }

    public IReadOnlyDictionary<LibraryItemCategory, double> CategoryScores { get; init; } =
        new Dictionary<LibraryItemCategory, double>();

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public bool ShouldInclude => IgnoreConfidence < 0.70d;
}

/// <summary>Classifies candidates using local, auditable signals only.</summary>
public interface IExecutableClassifier
{
    ExecutableClassificationResult Classify(ExecutableClassificationInput input);
}

/// <summary>
/// Conservative, deterministic executable classifier.  It intentionally prefers
/// an unknown result over pretending an arbitrary binary is a game.
/// </summary>
public sealed class ExecutableClassifier : IExecutableClassifier
{
    private static readonly string[] IgnoreTokens =
    {
        "uninstall", "unins", "setup", "installer", "installshield", "msiexec",
        "update", "updater", "crashreport", "crashpad", "crashhandler",
        "helper", "bootstrap", "redist", "vcredist", "dxsetup", "easyanticheat",
        "battleye", "anticheat", "service", "driver",
    };

    private static readonly string[] LauncherTokens =
    {
        "steam", "epicgameslauncher", "gog galaxy", "ubisoft connect", "ea app",
        "battle.net", "launcher",
    };

    private static readonly string[] DevelopmentTokens =
    {
        "visual studio", "rider", "intellij", "android studio", "code", "compiler",
        "ide", "sdk",
    };

    private static readonly string[] MediaTokens =
    {
        "blender", "photoshop", "premiere", "audacity", "obs", "vlc", "media",
        "editor", "studio",
    };

    private static readonly string[] UtilityTokens =
    {
        "7-zip", "7zip", "utility", "tool", "manager", "terminal", "powershell",
        "notepad", "calculator",
    };

    /// <inheritdoc />
    public ExecutableClassificationResult Classify(ExecutableClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var scores = Enum.GetValues<LibraryItemCategory>()
            .ToDictionary(category => category, _ => 0d);
        var reasons = new List<string>();
        var fileName = (input.FileName ?? Path.GetFileName(input.FilePath)).Trim();
        var searchable = string.Join(
            ' ',
            new[]
            {
                fileName,
                input.FileDescription,
                input.ProductName,
                input.CompanyName,
                input.ParentDirectoryName,
                input.ProviderId,
            }.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

        var ignoreConfidence = 0d;
        if (IsWindowsInfrastructurePath(input.FilePath))
        {
            ignoreConfidence = 0.99d;
            reasons.Add("The executable is located in a protected Windows infrastructure directory.");
        }

        if (ContainsAny(searchable, IgnoreTokens))
        {
            ignoreConfidence = Math.Max(ignoreConfidence, 0.96d);
            reasons.Add("The executable name or metadata indicates an installer, helper, updater, or runtime component.");
        }

        if (ignoreConfidence >= 0.70d)
        {
            scores[LibraryItemCategory.Unknown] = 0.01d;
            return new ExecutableClassificationResult
            {
                Category = LibraryItemCategory.Unknown,
                Confidence = 0.01d,
                IgnoreConfidence = ignoreConfidence,
                CategoryScores = scores,
                Reasons = reasons,
            };
        }

        if (input.DeclaredCategory is { } declaredCategory)
        {
            scores[declaredCategory] = 0.94d;
            reasons.Add("A trusted provider supplied a category.");
        }

        if (input.IsFromLauncherManifest)
        {
            scores[LibraryItemCategory.Game] = Math.Max(scores[LibraryItemCategory.Game], 0.98d);
            reasons.Add("The executable is associated with an installed launcher manifest.");
        }
        else if (input.HasGameEngineEvidence)
        {
            scores[LibraryItemCategory.Game] = Math.Max(scores[LibraryItemCategory.Game], 0.86d);
            reasons.Add("The surrounding installation contains game-engine evidence.");
        }

        if (ContainsAny(searchable, LauncherTokens))
        {
            scores[LibraryItemCategory.Launcher] = Math.Max(scores[LibraryItemCategory.Launcher], 0.88d);
            reasons.Add("The executable metadata matches a known launcher pattern.");
        }

        if (ContainsAny(searchable, DevelopmentTokens))
        {
            scores[LibraryItemCategory.DevelopmentTool] = Math.Max(scores[LibraryItemCategory.DevelopmentTool], 0.84d);
            reasons.Add("The executable metadata matches a development-tool pattern.");
        }

        if (ContainsAny(searchable, MediaTokens))
        {
            scores[LibraryItemCategory.MediaSoftware] = Math.Max(scores[LibraryItemCategory.MediaSoftware], 0.80d);
            reasons.Add("The executable metadata matches a media-software pattern.");
        }

        if (ContainsAny(searchable, UtilityTokens))
        {
            scores[LibraryItemCategory.Utility] = Math.Max(scores[LibraryItemCategory.Utility], 0.73d);
            reasons.Add("The executable metadata matches a utility pattern.");
        }

        if (input.IsRegisteredInstallation || input.IsStartMenuTarget)
        {
            scores[LibraryItemCategory.Application] = Math.Max(scores[LibraryItemCategory.Application], 0.76d);
            reasons.Add("The executable is exposed through a Windows application entry or Start-menu shortcut.");
        }

        if (scores.Values.All(score => score <= 0d))
        {
            scores[LibraryItemCategory.Unknown] = 0.55d;
            reasons.Add("No high-confidence local classification signal was available.");
        }

        var best = scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .First();

        return new ExecutableClassificationResult
        {
            Category = best.Key,
            Confidence = best.Value,
            IgnoreConfidence = ignoreConfidence,
            CategoryScores = scores,
            Reasons = reasons,
        };
    }

    private static bool ContainsAny(string value, IEnumerable<string> tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWindowsInfrastructurePath(string? path)
    {
        if (!PathNormalizer.TryNormalize(path, out var normalized))
        {
            return false;
        }

        return normalized.Contains("\\Windows\\System32\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\Windows\\SysWOW64\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\Windows\\WinSxS\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\Windows\\System32\\drivers\\", StringComparison.OrdinalIgnoreCase);
    }
}
