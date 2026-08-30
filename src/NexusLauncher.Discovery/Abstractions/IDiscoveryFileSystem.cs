namespace NexusLauncher.Discovery.Abstractions;

/// <summary>
/// Minimal filesystem surface used by discovery providers.  Keeping this surface
/// narrow makes providers testable without requiring a real Steam or Start-menu
/// installation.
/// </summary>
public interface IDiscoveryFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
}

/// <summary>Production filesystem implementation backed by <see cref="File"/> and <see cref="Directory"/>.</summary>
public sealed class PhysicalDiscoveryFileSystem : IDiscoveryFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.EnumerateFiles(path, searchPattern, searchOption);
    }
}
