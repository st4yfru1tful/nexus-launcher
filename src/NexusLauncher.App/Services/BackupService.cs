using System.IO.Compression;
using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class BackupService
{
    private const long MaximumEntrySize = 64L * 1024 * 1024;
    private static readonly HashSet<string> SupportedEntryNames = new(StringComparer.Ordinal)
    {
        "library.json",
        "settings.json"
    };

    // The app normally has one instance, but a shared gate also protects backup operations
    // created by future callers in the same process.
    private static readonly SemaphoreSlim BackupGate = new(1, 1);
    private readonly string _dataDirectory;
    private readonly string _libraryFile;
    private readonly string _settingsFile;

    public BackupService()
        : this(NexusPaths.DataDirectory)
    {
    }

    internal BackupService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _libraryFile = Path.Combine(_dataDirectory, "library.json");
        _settingsFile = Path.Combine(_dataDirectory, "settings.json");
    }

    public string CreateLibraryBackup(string targetFile)
    {
        BackupGate.Wait();
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            if (!File.Exists(_libraryFile))
            {
                throw new InvalidOperationException("There is no library data to export yet.");
            }

            var targetPath = GetBackupTargetPath(targetFile);
            var temporaryPath = GetTemporaryFilePath(Path.GetDirectoryName(targetPath)!, "backup");
            try
            {
                using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(_libraryFile, "library.json", CompressionLevel.Optimal);
                    if (File.Exists(_settingsFile))
                    {
                        archive.CreateEntryFromFile(_settingsFile, "settings.json", CompressionLevel.Optimal);
                    }
                }

                ReplaceFileWithoutDeletingExisting(temporaryPath, targetPath);
                return targetPath;
            }
            finally
            {
                DeleteFileIfPresent(temporaryPath);
            }
        }
        finally
        {
            BackupGate.Release();
        }
    }

    public void RestoreLibraryBackup(string backupFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFile);

        BackupGate.Wait();
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var backupPath = Path.GetFullPath(backupFile);
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException("The selected backup file was not found.", backupPath);
            }

            using var archive = ZipFile.OpenRead(backupPath);
            var entries = ValidateBackupEntries(archive);
            var stagingDirectory = CreateStagingDirectory();
            try
            {
                var stagedFiles = entries
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => StageAndValidateEntry(pair.Key, pair.Value, stagingDirectory))
                    .ToList();

                CommitStagedFiles(stagedFiles);
            }
            finally
            {
                DeleteDirectoryIfPresent(stagingDirectory);
            }
        }
        finally
        {
            BackupGate.Release();
        }
    }

    private string GetBackupTargetPath(string targetFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFile);
        var targetPath = Path.GetFullPath(targetFile);
        if (Path.EndsInDirectorySeparator(targetPath) || Directory.Exists(targetPath))
        {
            throw new ArgumentException("Choose a backup file, not a directory.", nameof(targetFile));
        }

        if (string.Equals(targetPath, _libraryFile, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetPath, _settingsFile, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A backup cannot replace Nexus library or settings data.", nameof(targetFile));
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException("The selected backup folder does not exist.");
        }

        return targetPath;
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateBackupEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!SupportedEntryNames.Contains(entry.FullName) || !string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The selected archive contains an unsupported backup entry.");
            }

            if (entry.Length > MaximumEntrySize)
            {
                throw new InvalidDataException($"The backup entry '{entry.FullName}' is too large.");
            }

            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException($"The selected archive contains duplicate '{entry.FullName}' entries.");
            }
        }

        if (!entries.ContainsKey("library.json"))
        {
            throw new InvalidDataException("The selected archive does not contain a library.json backup.");
        }

        return entries;
    }

    private StagedFile StageAndValidateEntry(string entryName, ZipArchiveEntry entry, string stagingDirectory)
    {
        var stagedPath = Path.Combine(stagingDirectory, entryName);
        try
        {
            using (var source = entry.Open())
            using (var destination = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                CopyEntryWithSizeLimit(source, destination, entryName, entry.Length);
                destination.Flush(flushToDisk: true);
            }

            ValidateStoredData(stagedPath, entryName);
            return new StagedFile(stagedPath, GetDataFilePath(entryName));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The backup entry '{entryName}' is not valid JSON.", exception);
        }
    }

    private string GetDataFilePath(string entryName)
    {
        return entryName switch
        {
            "library.json" => _libraryFile,
            "settings.json" => _settingsFile,
            _ => throw new InvalidDataException("The selected archive contains an unsupported backup entry.")
        };
    }

    private static void CopyEntryWithSizeLimit(Stream source, Stream destination, string entryName, long expectedLength)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            written = checked(written + read);
            if (written > MaximumEntrySize)
            {
                throw new InvalidDataException($"The backup entry '{entryName}' is too large.");
            }

            destination.Write(buffer, 0, read);
        }

        if (written != expectedLength)
        {
            throw new InvalidDataException($"The backup entry '{entryName}' has an unexpected length.");
        }
    }

    private static void ValidateStoredData(string stagedPath, string entryName)
    {
        object? normalizedData;
        using (var stream = File.OpenRead(stagedPath))
        {
            using (var document = JsonDocument.Parse(stream))
            {
                var expectedKind = string.Equals(entryName, "library.json", StringComparison.Ordinal)
                    ? JsonValueKind.Array
                    : JsonValueKind.Object;
                if (document.RootElement.ValueKind != expectedKind)
                {
                    throw new InvalidDataException($"The backup entry '{entryName}' has an invalid JSON shape.");
                }
            }

            stream.Position = 0;
            normalizedData = entryName switch
            {
                "library.json" => JsonSerializer.Deserialize<List<LibraryItem>>(stream, NexusJsonOptions.Default)?.OfType<LibraryItem>().ToList(),
                "settings.json" => JsonSerializer.Deserialize<AppSettings>(stream, NexusJsonOptions.Default),
                _ => null
            };
        }

        if (normalizedData is null)
        {
            throw new InvalidDataException($"The backup entry '{entryName}' cannot be read by this version of Nexus.");
        }

        WriteNormalizedStoredData(stagedPath, normalizedData);
    }

    private static void WriteNormalizedStoredData(string stagedPath, object data)
    {
        using var stream = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream, data, data.GetType(), NexusJsonOptions.Default);
        stream.Flush(flushToDisk: true);
    }

    private static void CommitStagedFiles(IReadOnlyList<StagedFile> stagedFiles)
    {
        var committedFiles = new List<CommittedFile>();
        var completed = false;
        try
        {
            foreach (var stagedFile in stagedFiles)
            {
                if (File.Exists(stagedFile.TargetPath))
                {
                    var rollbackPath = GetTemporaryFilePath(Path.GetDirectoryName(stagedFile.TargetPath)!, "restore-rollback");
                    File.Replace(stagedFile.StagedPath, stagedFile.TargetPath, rollbackPath, ignoreMetadataErrors: true);
                    committedFiles.Add(new CommittedFile(stagedFile.TargetPath, rollbackPath));
                }
                else
                {
                    File.Move(stagedFile.StagedPath, stagedFile.TargetPath);
                    committedFiles.Add(new CommittedFile(stagedFile.TargetPath, null));
                }
            }

            completed = true;
        }
        catch (Exception commitException)
        {
            var rollbackExceptions = RollBackCommittedFiles(committedFiles);
            if (rollbackExceptions.Count > 0)
            {
                throw new InvalidOperationException(
                    "Restore failed and Nexus could not fully roll back the existing data. Recovery files were left beside the data files.",
                    new AggregateException([commitException, .. rollbackExceptions]));
            }

            throw;
        }
        finally
        {
            if (completed)
            {
                foreach (var committedFile in committedFiles)
                {
                    if (committedFile.RollbackPath is not null)
                    {
                        DeleteFileIfPresent(committedFile.RollbackPath);
                    }
                }
            }
        }
    }

    private static List<Exception> RollBackCommittedFiles(IEnumerable<CommittedFile> committedFiles)
    {
        var exceptions = new List<Exception>();
        foreach (var committedFile in committedFiles.Reverse())
        {
            try
            {
                if (committedFile.RollbackPath is not null)
                {
                    File.Replace(committedFile.RollbackPath, committedFile.TargetPath, null, ignoreMetadataErrors: true);
                }
                else if (File.Exists(committedFile.TargetPath))
                {
                    File.Delete(committedFile.TargetPath);
                }
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        return exceptions;
    }

    private string CreateStagingDirectory()
    {
        var stagingDirectory = Path.Combine(_dataDirectory, $".restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        return stagingDirectory;
    }

    private static string GetTemporaryFilePath(string directory, string purpose)
    {
        return Path.Combine(directory, $".{purpose}-{Guid.NewGuid():N}.tmp");
    }

    private static void ReplaceFileWithoutDeletingExisting(string sourcePath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            File.Replace(sourcePath, targetPath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(sourcePath, targetPath);
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record StagedFile(string StagedPath, string TargetPath);

    private sealed record CommittedFile(string TargetPath, string? RollbackPath);
}
