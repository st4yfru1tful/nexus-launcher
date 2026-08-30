namespace NexusLauncher.Core.Paths;

/// <summary>
/// Normalizes local Windows icon source paths without allowing a shell icon
/// lookup to reach a UNC path, file URI, or mapped network drive.
/// </summary>
public static class IconPathNormalizer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".dll",
        ".ico"
    };

    /// <summary>
    /// Accepts a local icon source such as <c>C:\Apps\Game.exe</c> or
    /// <c>"C:\Apps\Game.exe",0</c>. The path need not exist yet.
    /// </summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryExtractPath(value, out var candidate) ||
            candidate.StartsWith(@"\\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(candidate) ||
            !PathNormalizer.TryNormalize(candidate, out var fullPath) ||
            fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            !SupportedExtensions.Contains(Path.GetExtension(fullPath)) ||
            IsNetworkDrive(fullPath))
        {
            return false;
        }

        normalized = fullPath;
        return true;
    }

    private static bool TryExtractPath(string? value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var candidate = Environment.ExpandEnvironmentVariables(value.Trim());
        if (candidate.StartsWith('"') || candidate.StartsWith('\''))
        {
            var quote = candidate[0];
            var closingQuote = candidate.IndexOf(quote, 1);
            if (closingQuote < 2) return false;
            candidate = candidate[1..closingQuote];
        }
        else
        {
            var comma = candidate.LastIndexOf(',');
            if (comma >= 0 &&
                int.TryParse(candidate[(comma + 1)..].Trim(), out _))
            {
                candidate = candidate[..comma];
            }
        }

        candidate = candidate.Trim();
        if (candidate.Length == 0 || candidate.Contains('\0')) return false;
        path = candidate;
        return true;
    }

    private static bool IsNetworkDrive(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return string.IsNullOrWhiteSpace(root) || new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
