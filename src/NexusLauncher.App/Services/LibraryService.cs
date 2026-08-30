using System.Diagnostics;
using NexusLauncher.App.Models;
using NexusLauncher.Core.Discovery;

namespace NexusLauncher.App.Services;

public sealed class LibraryService(LibraryRepository repository, DiscoveryService discovery)
{
    private readonly LibraryRepository _repository = repository;
    private readonly DiscoveryService _discovery = discovery;

    public Task<List<LibraryItem>> LoadAsync() => _repository.LoadAsync();
    public Task SaveAsync(IEnumerable<LibraryItem> items) => _repository.SaveAsync(items);

    public static LibraryItem CreateManualItem(string executable) => ExecutableInspector.CreateFromExecutable(executable, true);

    public async Task<ScanResult> ScanAndMergeAsync(IList<LibraryItem> current, AppSettings settings, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var discovery = await _discovery.DiscoverAsync(settings, progress, cancellationToken);
        var existingKeys = new HashSet<string>(current.Select(Identity), StringComparer.OrdinalIgnoreCase);
        // Discovery applies this filter too. Keeping it at the merge boundary makes a
        // user removal durable even if a future discovery provider omits that filter.
        var added = discovery.Items
            .Where(item => !LibrarySuppression.IsSuppressed(settings, item))
            .Where(item => existingKeys.Add(Identity(item)))
            .ToList();
        foreach (var item in added)
        {
            current.Add(item);
        }

        await SaveAsync(current);
        return new ScanResult(discovery.Items.Count, added.Count, discovery.Issues);
    }

    public static Task LaunchAsync(LibraryItem item)
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
        return Task.CompletedTask;
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

public readonly record struct ScanResult(int ItemsFound, int ItemsAdded, IReadOnlyList<DiscoveryIssue> Issues);
