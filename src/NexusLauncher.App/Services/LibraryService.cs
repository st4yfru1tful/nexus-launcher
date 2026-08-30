using System.Diagnostics;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class LibraryService(LibraryRepository repository, DiscoveryService discovery, ExecutableInspector inspector)
{
    private readonly LibraryRepository _repository = repository;
    private readonly DiscoveryService _discovery = discovery;
    private readonly ExecutableInspector _inspector = inspector;

    public Task<List<LibraryItem>> LoadAsync() => _repository.LoadAsync();
    public Task SaveAsync(IEnumerable<LibraryItem> items) => _repository.SaveAsync(items);

    public LibraryItem CreateManualItem(string executable) => _inspector.CreateFromExecutable(executable, true);

    public async Task<ScanResult> ScanAndMergeAsync(IList<LibraryItem> current, AppSettings settings, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var discovered = await _discovery.DiscoverAsync(settings, progress, cancellationToken);
        var existingKeys = new HashSet<string>(current.Select(Identity), StringComparer.OrdinalIgnoreCase);
        var added = discovered.Where(item => existingKeys.Add(Identity(item))).ToList();
        foreach (var item in added)
        {
            current.Add(item);
        }

        await SaveAsync(current);
        return new ScanResult(discovered.Count, added.Count);
    }

    public async Task LaunchAsync(LibraryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.LaunchUri))
        {
            Process.Start(new ProcessStartInfo(item.LaunchUri) { UseShellExecute = true });
        }
        else if (!string.IsNullOrWhiteSpace(item.ExecutablePath) && File.Exists(item.ExecutablePath))
        {
            var start = new ProcessStartInfo(item.ExecutablePath, item.LaunchArguments ?? string.Empty)
            {
                UseShellExecute = true,
                WorkingDirectory = Directory.Exists(item.WorkingDirectory) ? item.WorkingDirectory : Path.GetDirectoryName(item.ExecutablePath) ?? string.Empty
            };
            Process.Start(start);
        }
        else
        {
            throw new FileNotFoundException("Nexus could not find a launchable path for this item.");
        }

        item.LastPlayed = DateTimeOffset.Now;
        await Task.CompletedTask;
    }

    public static void OpenFolder(LibraryItem item)
    {
        var folder = item.InstallPath;
        if (!Directory.Exists(folder)) folder = Path.GetDirectoryName(item.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return;
        }

        throw new DirectoryNotFoundException("Nexus could not find this installation folder.");
    }

    private static string Identity(LibraryItem item) =>
        !string.IsNullOrWhiteSpace(item.LaunchUri) ? item.LaunchUri :
        !string.IsNullOrWhiteSpace(item.ExecutablePath) ? Path.GetFullPath(item.ExecutablePath).TrimEnd('\\') :
        $"{item.Name}|{item.InstallPath}";
}

public readonly record struct ScanResult(int ItemsFound, int ItemsAdded);
