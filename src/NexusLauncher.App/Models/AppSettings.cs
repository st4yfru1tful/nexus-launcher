namespace NexusLauncher.App.Models;

public enum AppTheme
{
    Dark,
    Light,
    System
}

public sealed class AppSettings
{
    public bool HasCompletedOnboarding { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool EnableAnimations { get; set; } = true;
    public bool IncludeStartMenuShortcuts { get; set; } = true;
    public bool IncludeInstalledApplications { get; set; } = true;
    public List<string> ScanFolders { get; set; } = [];
    public List<string> IgnoredPaths { get; set; } = [];
}
