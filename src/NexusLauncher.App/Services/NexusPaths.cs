namespace NexusLauncher.App.Services;

public static class NexusPaths
{
    public const string PortableModeMarkerFileName = "NexusLauncher.portable";
    public const string PortableDataDirectoryName = "NexusLauncherData";

    // The marker is deliberately evaluated once at process start. A portable copy
    // cannot start by reading an installed copy's LocalAppData files and then
    // switch roots if a file changes underneath it.
    public static bool IsPortableMode { get; } = HasPortableModeMarker(AppContext.BaseDirectory, File.Exists);
    public static string Root { get; } = ResolveRoot(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        IsPortableMode);
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

    internal static bool HasPortableModeMarker(string applicationDirectory, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);
        return fileExists(Path.Combine(applicationDirectory, PortableModeMarkerFileName));
    }

    internal static string ResolveRoot(string applicationDirectory, string localApplicationData, bool isPortableMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);

        return isPortableMode
            ? Path.Combine(applicationDirectory, PortableDataDirectoryName)
            : Path.Combine(localApplicationData, "NexusLauncher");
    }
}
