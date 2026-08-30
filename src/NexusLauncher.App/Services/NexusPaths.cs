namespace NexusLauncher.App.Services;

public static class NexusPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusLauncher");
    public static string DataDirectory { get; } = Path.Combine(Root, "data");
    public static string CacheDirectory { get; } = Path.Combine(Root, "cache");
    public static string LogsDirectory { get; } = Path.Combine(Root, "logs");
    public static string LibraryFile { get; } = Path.Combine(DataDirectory, "library.json");
    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
