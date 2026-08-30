using System.Diagnostics;
using System.Text.RegularExpressions;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class WingetStoreService
{
    public async Task<bool> IsAvailableAsync()
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

    public async Task<IReadOnlyList<StorePackage>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var output = await RunAsync($"search --query \"{EscapeArgument(query)}\" --accept-source-agreements --disable-interactivity", cancellationToken);
        if (output.ExitCode != 0) return [];
        return ParseSearchResults(output.StandardOutput);
    }

    public Process StartInstall(StorePackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id)) throw new ArgumentException("A WinGet package identifier is required.", nameof(package));
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

    private static IReadOnlyList<StorePackage> ParseSearchResults(string output)
    {
        var results = new List<StorePackage>();
        var headerSeen = false;
        foreach (var line in output.Replace("\r", string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (!headerSeen)
            {
                headerSeen = Regex.IsMatch(trimmed, "^Name\\s+Id\\s+Version\\s+Source", RegexOptions.IgnoreCase);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.All(character => character is '-' or ' ')) continue;
            var columns = Regex.Split(trimmed, "\\s{2,}");
            if (columns.Length < 3) continue;
            var source = columns.Length >= 4 ? columns[^1] : "winget";
            var version = columns.Length >= 4 ? columns[^2] : string.Empty;
            var id = columns.Length >= 4 ? columns[^3] : columns[^2];
            var name = string.Join(" ", columns.Take(columns.Length >= 4 ? columns.Length - 3 : columns.Length - 2));
            if (string.IsNullOrWhiteSpace(id) || !id.Contains('.', StringComparison.Ordinal)) continue;
            results.Add(new StorePackage { Name = name, Id = id, Version = version, Source = source });
        }

        return results.Take(40).ToList();
    }

    private static string EscapeArgument(string input) => input.Replace("\"", string.Empty);
    private sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);
}
