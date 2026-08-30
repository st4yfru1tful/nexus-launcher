using System.Text;

namespace NexusLauncher.App.Services;

public sealed class DiagnosticsService
{
    public string GetSummary(int libraryCount) =>
        $"Nexus Launcher {typeof(DiagnosticsService).Assembly.GetName().Version}\n" +
        $"Windows: {Environment.OSVersion.VersionString}\n" +
        $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n" +
        $"Library items: {libraryCount}\n" +
        $"Data folder: {NexusPaths.Root}\n" +
        $"UTC: {DateTimeOffset.UtcNow:O}";

    public string ExportReport(int libraryCount)
    {
        NexusPaths.EnsureCreated();
        var path = Path.Combine(NexusPaths.LogsDirectory, $"diagnostic-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, GetSummary(libraryCount), Encoding.UTF8);
        return path;
    }
}
