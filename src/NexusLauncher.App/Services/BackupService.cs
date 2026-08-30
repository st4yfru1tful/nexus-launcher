using System.IO.Compression;

namespace NexusLauncher.App.Services;

public sealed class BackupService
{
    public string CreateLibraryBackup(string targetFile)
    {
        NexusPaths.EnsureCreated();
        if (!File.Exists(NexusPaths.LibraryFile)) throw new InvalidOperationException("There is no library data to export yet.");
        if (File.Exists(targetFile)) File.Delete(targetFile);
        using var archive = ZipFile.Open(targetFile, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(NexusPaths.LibraryFile, "library.json", CompressionLevel.Optimal);
        if (File.Exists(NexusPaths.SettingsFile)) archive.CreateEntryFromFile(NexusPaths.SettingsFile, "settings.json", CompressionLevel.Optimal);
        return targetFile;
    }

    public void RestoreLibraryBackup(string backupFile)
    {
        NexusPaths.EnsureCreated();
        using var archive = ZipFile.OpenRead(backupFile);
        foreach (var name in new[] { "library.json", "settings.json" })
        {
            var entry = archive.GetEntry(name);
            if (entry is null) continue;
            var target = Path.Combine(NexusPaths.DataDirectory, name);
            entry.ExtractToFile(target, true);
        }
    }
}
