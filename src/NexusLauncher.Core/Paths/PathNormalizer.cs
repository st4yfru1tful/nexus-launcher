namespace NexusLauncher.Core.Paths;

/// <summary>
/// Centralizes Windows path comparison rules.  Discovery data comes from sources
/// that disagree on quotes, separator style, casing, and trailing separators;
/// only normalized paths should participate in identity matching.
/// </summary>
public static class PathNormalizer
{
    /// <summary>Windows filesystems used by Nexus are compared case-insensitively.</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Normalizes a potentially quoted Windows path.  The path need not exist.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty or malformed.</exception>
    public static string Normalize(string path)
    {
        if (!TryNormalize(path, out var normalized))
        {
            throw new ArgumentException("The supplied value is not a valid path.", nameof(path));
        }

        return normalized;
    }

    /// <summary>Attempts to normalize a potentially quoted Windows path.</summary>
    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = TrimWrappingQuotes(path.Trim());
        if (candidate.Length == 0)
        {
            return false;
        }

        candidate = Environment.ExpandEnvironmentVariables(candidate);
        candidate = RemoveExtendedPathPrefix(candidate);

        try
        {
            candidate = candidate.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            return normalized.Length > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Determines whether two paths resolve to the same normalized path.</summary>
    public static bool AreEquivalent(string? left, string? right)
    {
        return TryNormalize(left, out var normalizedLeft) &&
               TryNormalize(right, out var normalizedRight) &&
               Comparer.Equals(normalizedLeft, normalizedRight);
    }

    /// <summary>
    /// Determines whether <paramref name="candidate"/> is the same as, or is
    /// contained by, <paramref name="parent"/>.  This uses a boundary check so
    /// <c>C:\\Apps2</c> is not considered part of <c>C:\\Apps</c>.
    /// </summary>
    public static bool IsDescendantOrSelf(string? candidate, string? parent)
    {
        if (!TryNormalize(candidate, out var normalizedCandidate) ||
            !TryNormalize(parent, out var normalizedParent))
        {
            return false;
        }

        if (Comparer.Equals(normalizedCandidate, normalizedParent))
        {
            return true;
        }

        var prefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimWrappingQuotes(string value)
    {
        return value.Length >= 2 &&
               ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1].Trim()
            : value;
    }

    private static string RemoveExtendedPathPrefix(string value)
    {
        const string ExtendedUncPrefix = @"\\?\UNC\";
        const string ExtendedPrefix = @"\\?\";

        if (value.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + value[ExtendedUncPrefix.Length..];
        }

        return value.StartsWith(ExtendedPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[ExtendedPrefix.Length..]
            : value;
    }
}
