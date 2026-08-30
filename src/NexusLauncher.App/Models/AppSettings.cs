namespace NexusLauncher.App.Models;

public enum AppTheme
{
    Dark,
    Light,
    System
}

public sealed class AppSettings
{
    private List<string> _scanFolders = [];
    private List<string> _ignoredPaths = [];
    private List<string> _ignoredIdentities = [];

    public bool HasCompletedOnboarding { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool EnableAnimations { get; set; } = true;
    public bool IncludeStartMenuShortcuts { get; set; } = true;
    public bool IncludeInstalledApplications { get; set; } = true;
    public List<string> ScanFolders { get => _scanFolders; set => _scanFolders = SanitizeStrings(value); }
    public List<string> IgnoredPaths { get => _ignoredPaths; set => _ignoredPaths = SanitizeStrings(value); }
    public List<string> IgnoredIdentities { get => _ignoredIdentities; set => _ignoredIdentities = SanitizeStrings(value); }

    private static List<string> SanitizeStrings(IEnumerable<string>? values) => values?.OfType<string>().ToList() ?? [];
}
