using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

/// <summary>
/// Maintains the local record of items the user has explicitly removed from Nexus.
/// These keys are intentionally based on launch locations rather than transient IDs so
/// a later discovery scan cannot silently put an item back in the library.
/// </summary>
public static class LibrarySuppression
{
    private const string UriPrefix = "uri:";
    private const string ExecutablePrefix = "exe:";
    private const string InstallPrefix = "install:";
    private const string NamePrefix = "name:";

    /// <summary>Records every durable identifier available for an item the user removed.</summary>
    public static void Suppress(AppSettings settings, LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(item);
        EnsureCollections(settings);

        foreach (var identity in GetIdentities(item))
        {
            AddUnique(settings.IgnoredIdentities, identity);
        }

        foreach (var path in GetPaths(item))
        {
            AddUnique(settings.IgnoredPaths, path);
        }
    }

    /// <summary>Returns whether a scan candidate matches a user-removed item.</summary>
    public static bool IsSuppressed(AppSettings settings, LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(item);

        var ignoredIdentities = settings.IgnoredIdentities ?? [];
        if (GetIdentities(item).Any(identity => ignoredIdentities.Contains(identity, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        var ignoredPaths = settings.IgnoredPaths ?? [];
        return GetPaths(item).Any(candidate => ignoredPaths
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Any(ignored => IsSamePathOrDescendant(candidate, ignored!)));
    }

    /// <summary>
    /// Clears matching local suppression keys before a user deliberately adds an executable.
    /// A URI-only source may remain suppressed, which prevents it from creating a duplicate of
    /// the manual entry while still keeping the user-selected executable available.
    /// </summary>
    public static bool RestoreManualAddition(AppSettings settings, LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(item);
        EnsureCollections(settings);

        var identities = new HashSet<string>(GetIdentities(item), StringComparer.OrdinalIgnoreCase);
        var nameIdentity = GetNameIdentity(item);
        if (nameIdentity is not null)
        {
            identities.Add(nameIdentity);
        }

        var paths = GetPaths(item);
        var removed = settings.IgnoredIdentities.RemoveAll(identity =>
            identities.Contains(identity) || IsPathIdentityOverlapping(identity, paths)) > 0;
        removed |= settings.IgnoredPaths.RemoveAll(ignored =>
        {
            var normalizedIgnored = NormalizePath(ignored);
            return normalizedIgnored is not null && paths.Any(path =>
                IsSamePathOrDescendant(path, normalizedIgnored) || IsSamePathOrDescendant(normalizedIgnored, path));
        }) > 0;
        return removed;
    }

    /// <summary>Returns stable identifiers used to compare a candidate with a removed item.</summary>
    public static IReadOnlyList<string> GetIdentities(LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var identities = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.LaunchUri))
        {
            identities.Add(UriPrefix + item.LaunchUri.Trim());
        }

        var executablePath = NormalizePath(item.ExecutablePath);
        if (executablePath is not null)
        {
            identities.Add(ExecutablePrefix + executablePath);
        }

        var installPath = NormalizePath(item.InstallPath);
        if (installPath is not null)
        {
            identities.Add(InstallPrefix + installPath);
        }

        // Some registered applications have neither a launch URI nor a usable path. In that
        // case the display name is the only stable local identity available.
        if (identities.Count == 0)
        {
            var nameIdentity = GetNameIdentity(item);
            if (nameIdentity is not null)
            {
                identities.Add(nameIdentity);
            }
        }

        return identities;
    }

    /// <summary>Returns normalized local paths that can be safely matched at directory boundaries.</summary>
    public static IReadOnlyList<string> GetPaths(LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new[] { NormalizePath(item.ExecutablePath), NormalizePath(item.InstallPath) }
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetNameIdentity(LibraryItem item)
    {
        var name = item.Name?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : NamePrefix + name;
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static void EnsureCollections(AppSettings settings)
    {
        settings.IgnoredIdentities ??= [];
        settings.IgnoredPaths ??= [];
    }

    private static bool IsPathIdentityOverlapping(string identity, IReadOnlyList<string> paths)
    {
        var storedPath = identity.StartsWith(ExecutablePrefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizePath(identity[ExecutablePrefix.Length..])
            : identity.StartsWith(InstallPrefix, StringComparison.OrdinalIgnoreCase)
                ? NormalizePath(identity[InstallPrefix.Length..])
                : null;
        return storedPath is not null && paths.Any(path =>
            IsSamePathOrDescendant(path, storedPath) || IsSamePathOrDescendant(storedPath, path));
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(path.Trim().Trim('"'));
            return TrimTrailingSeparators(normalized);
        }
        catch (ArgumentException)
        {
            return TrimTrailingSeparators(path.Trim().Trim('"'));
        }
        catch (NotSupportedException)
        {
            return TrimTrailingSeparators(path.Trim().Trim('"'));
        }
    }

    private static string TrimTrailingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? root ?? path : trimmed;
    }

    private static bool IsSamePathOrDescendant(string candidate, string root)
    {
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
