using System.Globalization;
using NexusLauncher.Core.Paths;
using NexusLauncher.Discovery.Steam.Vdf;

namespace NexusLauncher.Discovery.Steam;

/// <summary>A Steam library and the app ids presently recorded in it.</summary>
public sealed record SteamLibraryFolder
{
    public int Index { get; init; }

    public string Path { get; init; } = string.Empty;

    public IReadOnlyList<uint> AppIds { get; init; } = Array.Empty<uint>();
}

/// <summary>Reads both legacy and current libraryfolders.vdf layouts.</summary>
public static class SteamLibraryFoldersParser
{
    public static IReadOnlyList<SteamLibraryFolder> Parse(string text)
    {
        var document = SteamVdfParser.Parse(text);
        var libraries = document.Root.GetObject("libraryfolders") ?? document.Root;
        var folders = new List<SteamLibraryFolder>();

        foreach (var entry in libraries.Entries)
        {
            if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            var path = entry.Value.Children?.GetString("path") ?? entry.Value.Scalar;
            if (!PathNormalizer.TryNormalize(path, out var normalizedPath))
            {
                continue;
            }

            var appIds = entry.Value.Children?.GetObject("apps")?.Entries
                .Select(app => uint.TryParse(app.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
                    ? (uint?)appId
                    : null)
                .Where(appId => appId.HasValue)
                .Select(appId => appId!.Value)
                .OrderBy(appId => appId)
                .ToArray() ?? Array.Empty<uint>();

            folders.Add(new SteamLibraryFolder
            {
                Index = index,
                Path = normalizedPath,
                AppIds = appIds,
            });
        }

        return folders.OrderBy(folder => folder.Index).ToArray();
    }

    public static bool TryParse(string text, out IReadOnlyList<SteamLibraryFolder> folders)
    {
        try
        {
            folders = Parse(text);
            return true;
        }
        catch (VdfParseException)
        {
            folders = Array.Empty<SteamLibraryFolder>();
            return false;
        }
    }
}
