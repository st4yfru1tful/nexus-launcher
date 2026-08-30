using System.IO.Compression;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public static class ModArchiveService
{
    public static async Task<ModArchiveResult> ExtractSafelyAsync(string archivePath, string destination, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("The selected mod archive no longer exists.", archivePath);
        if (!Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Nexus currently supports safe ZIP mod archives. Other archive formats are intentionally not extracted automatically.");
        }

        return await Task.Run(() =>
        {
            var safeRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(safeRoot);
            var extracted = 0;
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name) && entry.FullName.EndsWith('/')) continue;
                var target = Path.GetFullPath(Path.Combine(safeRoot, entry.FullName));
                if (!target.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The archive contains a path that would write outside the selected mod folder.");
                }

                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                entry.ExtractToFile(target, true);
                extracted++;
            }

            return new ModArchiveResult(extracted, safeRoot);
        }, cancellationToken);
    }
}
