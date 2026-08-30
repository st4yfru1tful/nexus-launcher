using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

/// <summary>
/// Produces the only local context eligible for an AI metadata lookup.
/// The factory only derives path leaf names; it never reads local files or
/// carries launch details into the outbound request.
/// </summary>
public static class AiMetadataRequestFactory
{
    public static AiMetadataLookupRequest Create(LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var executablePath = IsFileSystemPath(item.ExecutablePath) ? item.ExecutablePath : null;
        return new AiMetadataLookupRequest
        {
            Title = CleanRequired(item.Name, "Untitled item"),
            Provider = CleanOptional(item.Provider),
            Publisher = CleanOptional(item.Publisher),
            Version = CleanOptional(item.Version),
            ExecutableFileName = GetLeafName(executablePath),
            ParentFolderName = GetParentFolderName(executablePath)
        };
    }

    private static string CleanRequired(string? value, string fallback) => CleanOptional(value) ?? fallback;

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsFileSystemPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.IsFile;
    }

    private static string? GetParentFolderName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        try
        {
            return GetLeafName(Path.GetDirectoryName(executablePath));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? GetLeafName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return CleanOptional(Path.GetFileName(normalized));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
