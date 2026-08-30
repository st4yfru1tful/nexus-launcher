using System.IO.Compression;
using System.Text.Json;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "NexusLauncher.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateLibraryBackup_with_valid_data_writes_library_and_settings_entries()
    {
        Directory.CreateDirectory(_dataDirectory);
        const string library = "[{\"Name\":\"Nexus\"}]";
        const string settings = "{\"Theme\":0}";
        File.WriteAllText(LibraryFile, library);
        File.WriteAllText(SettingsFile, settings);
        var backupFile = Path.Combine(_dataDirectory, "valid.nexusbackup");
        var service = new BackupService(_dataDirectory);

        service.CreateLibraryBackup(backupFile);

        using var archive = ZipFile.OpenRead(backupFile);
        Assert.Equal(library, ReadEntry(archive, "library.json"));
        Assert.Equal(settings, ReadEntry(archive, "settings.json"));
    }

    [Fact]
    public void CreateLibraryBackup_when_replacing_a_locked_backup_fails_preserves_existing_backup()
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(LibraryFile, "[]");
        var backupFile = Path.Combine(_dataDirectory, "existing.nexusbackup");
        File.WriteAllText(backupFile, "previous backup");
        var service = new BackupService(_dataDirectory);

        using (File.Open(backupFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => service.CreateLibraryBackup(backupFile));
        }

        Assert.Equal("previous backup", File.ReadAllText(backupFile));
    }

    [Fact]
    public void RestoreLibraryBackup_when_archive_contains_an_unsupported_entry_leaves_current_data_unchanged()
    {
        Directory.CreateDirectory(_dataDirectory);
        const string existingLibrary = "[{\"name\":\"Existing game\"}]";
        File.WriteAllText(LibraryFile, existingLibrary);
        var backupFile = Path.Combine(_dataDirectory, "malicious.nexusbackup");
        using (var archive = ZipFile.Open(backupFile, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "library.json", "[]");
            WriteEntry(archive, "../settings.json", "{}");
        }

        var service = new BackupService(_dataDirectory);

        Assert.Throws<InvalidDataException>(() => service.RestoreLibraryBackup(backupFile));

        Assert.Equal(existingLibrary, File.ReadAllText(LibraryFile));
    }

    [Fact]
    public void RestoreLibraryBackup_when_library_shape_is_not_deserializable_leaves_current_data_unchanged()
    {
        Directory.CreateDirectory(_dataDirectory);
        const string existingLibrary = "[{\"name\":\"Existing game\"}]";
        File.WriteAllText(LibraryFile, existingLibrary);
        var backupFile = Path.Combine(_dataDirectory, "invalid-shape.nexusbackup");
        using (var archive = ZipFile.Open(backupFile, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "library.json", "[42]");
        }

        var service = new BackupService(_dataDirectory);

        Assert.Throws<InvalidDataException>(() => service.RestoreLibraryBackup(backupFile));

        Assert.Equal(existingLibrary, File.ReadAllText(LibraryFile));
    }

    [Fact]
    public void RestoreLibraryBackup_when_a_later_replacement_fails_rolls_back_earlier_data()
    {
        Directory.CreateDirectory(_dataDirectory);
        const string existingLibrary = "[{\"name\":\"Existing game\"}]";
        const string existingSettings = "{\"theme\":\"dark\"}";
        File.WriteAllText(LibraryFile, existingLibrary);
        File.WriteAllText(SettingsFile, existingSettings);
        var backupFile = Path.Combine(_dataDirectory, "replacement.nexusbackup");
        using (var archive = ZipFile.Open(backupFile, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "library.json", "[{\"name\":\"Replacement game\"}]");
            WriteEntry(archive, "settings.json", "{\"theme\":\"light\"}");
        }

        var service = new BackupService(_dataDirectory);
        using (File.Open(SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => service.RestoreLibraryBackup(backupFile));
        }

        Assert.Equal(existingLibrary, File.ReadAllText(LibraryFile));
        Assert.Equal(existingSettings, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void RestoreLibraryBackup_with_valid_entries_replaces_the_backed_up_files()
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(LibraryFile, "[{\"Name\":\"Existing game\"}]");
        File.WriteAllText(SettingsFile, "{\"Theme\":0}");
        var backupFile = Path.Combine(_dataDirectory, "valid.nexusbackup");
        const string replacementLibrary = "[{\"Name\":\"Replacement game\"}]";
        const string replacementSettings = "{\"Theme\":1}";
        using (var archive = ZipFile.Open(backupFile, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "library.json", replacementLibrary);
            WriteEntry(archive, "settings.json", replacementSettings);
        }

        var service = new BackupService(_dataDirectory);

        service.RestoreLibraryBackup(backupFile);

        using var restoredLibrary = JsonDocument.Parse(File.ReadAllText(LibraryFile));
        Assert.Equal("Replacement game", restoredLibrary.RootElement[0].GetProperty("Name").GetString());
        using var restoredSettings = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        Assert.Equal(1, restoredSettings.RootElement.GetProperty("Theme").GetInt32());
    }

    [Fact]
    public void RestoreLibraryBackup_with_explicit_null_values_normalizes_runtime_collections()
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(LibraryFile, "[]");
        var backupFile = Path.Combine(_dataDirectory, "null-values.nexusbackup");
        using (var archive = ZipFile.Open(backupFile, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "library.json", "[null,{\"Name\":null,\"Tags\":[null,\"kept\"]}]");
            WriteEntry(archive, "settings.json", "{\"ScanFolders\":null,\"IgnoredPaths\":[null,\"C:\\\\Games\"],\"IgnoredIdentities\":null}");
        }

        var service = new BackupService(_dataDirectory);

        service.RestoreLibraryBackup(backupFile);

        using var library = JsonDocument.Parse(File.ReadAllText(LibraryFile));
        Assert.Equal(1, library.RootElement.GetArrayLength());
        var restoredItem = library.RootElement[0];
        Assert.Equal("Untitled item", restoredItem.GetProperty("Name").GetString());
        var tags = restoredItem.GetProperty("Tags");
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);
        Assert.Equal(1, tags.GetArrayLength());
        Assert.Equal("kept", tags[0].GetString());

        using var settings = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        Assert.Equal(0, settings.RootElement.GetProperty("ScanFolders").GetArrayLength());
        Assert.Equal(1, settings.RootElement.GetProperty("IgnoredPaths").GetArrayLength());
        Assert.Equal("C:\\Games", settings.RootElement.GetProperty("IgnoredPaths")[0].GetString());
        Assert.Equal(0, settings.RootElement.GetProperty("IgnoredIdentities").GetArrayLength());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string LibraryFile => Path.Combine(_dataDirectory, "library.json");

    private string SettingsFile => Path.Combine(_dataDirectory, "settings.json");

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
