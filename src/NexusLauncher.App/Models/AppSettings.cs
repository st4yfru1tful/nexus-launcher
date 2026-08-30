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
    private int _aiMonthlyRequestLimit = 25;
    private int _aiRequestsThisMonth;

    public bool HasCompletedOnboarding { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool EnableAnimations { get; set; } = true;
    public bool IncludeStartMenuShortcuts { get; set; } = true;
    public bool IncludeInstalledApplications { get; set; } = true;
    /// <summary>
    /// Explicit local consent for Nexus AI gateway metadata suggestions. This
    /// never stores a token or an OpenAI credential in settings.json.
    /// </summary>
    public bool EnableAiMetadata { get; set; }
    public int AiMonthlyRequestLimit
    {
        get => _aiMonthlyRequestLimit;
        set => _aiMonthlyRequestLimit = Math.Clamp(value, 1, 500);
    }
    public string? AiUsageMonth { get; set; }
    public int AiRequestsThisMonth
    {
        get => _aiRequestsThisMonth;
        set => _aiRequestsThisMonth = Math.Clamp(value, 0, 500);
    }
    public List<string> ScanFolders { get => _scanFolders; set => _scanFolders = SanitizeStrings(value); }
    public List<string> IgnoredPaths { get => _ignoredPaths; set => _ignoredPaths = SanitizeStrings(value); }
    public List<string> IgnoredIdentities { get => _ignoredIdentities; set => _ignoredIdentities = SanitizeStrings(value); }

    private static List<string> SanitizeStrings(IEnumerable<string>? values) => values?.OfType<string>().ToList() ?? [];
}
