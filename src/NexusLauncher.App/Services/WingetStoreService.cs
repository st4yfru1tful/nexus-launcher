using System.Diagnostics;
using System.Text.RegularExpressions;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public static class WingetStoreService
{
    private static readonly Regex PackageIdPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);

    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            var result = await RunAsync("--version", CancellationToken.None);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<IReadOnlyList<StorePackage>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var output = await RunAsync($"search --query \"{EscapeArgument(query)}\" --accept-source-agreements --disable-interactivity", cancellationToken);
        if (output.ExitCode != 0) return [];
        return ParseSearchResults(output.StandardOutput);
    }

    public static Process StartInstall(StorePackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id) || !PackageIdPattern.IsMatch(package.Id))
        {
            throw new ArgumentException("A valid WinGet package identifier is required.", nameof(package));
        }
        return Process.Start(new ProcessStartInfo("winget", $"install --id \"{EscapeArgument(package.Id)}\" --exact --accept-package-agreements --accept-source-agreements")
        {
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows could not start WinGet.");
    }

    private static async Task<ProcessOutput> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("winget", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessOutput(process.ExitCode, await stdout, await stderr);
    }

    internal static IReadOnlyList<StorePackage> ParseSearchResults(string output)
    {
        var results = new List<StorePackage>();
        SearchTableLayout? layout = null;
        foreach (var line in output.Replace("\r", string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (layout is null)
            {
                layout = SearchTableLayout.TryCreate(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.All(character => character is '-' or ' ')) continue;
            var name = layout.ReadColumn(line, "Name");
            var id = layout.ReadColumn(line, "Id");
            var version = layout.ReadColumn(line, "Version");
            var source = layout.ReadColumn(line, "Source");
            if (string.IsNullOrWhiteSpace(id) || !PackageIdPattern.IsMatch(id)) continue;
            results.Add(new StorePackage { Name = name, Id = id, Version = version, Source = source });
        }

        return results.Take(40).ToList();
    }

    /// <summary>
    /// WinGet writes a fixed-width table, but its headers have changed over time
    /// (notably adding a <c>Match</c> column). Slicing records from the header
    /// offsets is more reliable than splitting on whitespace because a short
    /// package name is followed by only one padding space.
    /// </summary>
    private sealed class SearchTableLayout
    {
        private static readonly string[] RequiredColumns = ["Name", "Id", "Version"];
        private static readonly string[] KnownColumns = ["Name", "Id", "Version", "Match", "Source"];

        private readonly IReadOnlyDictionary<string, int> _starts;

        private SearchTableLayout(IReadOnlyDictionary<string, int> starts) => _starts = starts;

        public static SearchTableLayout? TryCreate(string line)
        {
            var starts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in KnownColumns)
            {
                var match = Regex.Match(line, $"(?<!\\S){Regex.Escape(column)}(?!\\S)", RegexOptions.IgnoreCase);
                if (match.Success) starts[column] = match.Index;
            }

            if (RequiredColumns.Any(column => !starts.ContainsKey(column))) return null;
            if (starts["Name"] >= starts["Id"] || starts["Id"] >= starts["Version"]) return null;
            return new SearchTableLayout(starts);
        }

        public string ReadColumn(string line, string column)
        {
            if (!_starts.TryGetValue(column, out var start) || start >= line.Length) return column == "Source" ? "winget" : string.Empty;
            var end = _starts.Values.Where(nextStart => nextStart > start).DefaultIfEmpty(line.Length).Min();
            return line[start..Math.Min(end, line.Length)].Trim();
        }
    }

    private static string EscapeArgument(string input) => input.Replace("\"", string.Empty);
    private sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);
}
