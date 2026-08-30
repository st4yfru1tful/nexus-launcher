namespace NexusLauncher.App.Models;

public enum LibraryCategory
{
    Game,
    Application,
    Utility,
    DevelopmentTool,
    MediaSoftware,
    Launcher,
    Unknown
}

public sealed class LibraryItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled item";
    public LibraryCategory Category { get; set; } = LibraryCategory.Unknown;
    public string? ExecutablePath { get; set; }
    public string? LaunchUri { get; set; }
    public string? LaunchArguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? InstallPath { get; set; }
    public string? Provider { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? Version { get; set; }
    public string? IconPath { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset DateDiscovered { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayed { get; set; }
    public TimeSpan TotalPlaytime { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    public bool IsManual { get; set; }
    public long? InstallSizeBytes { get; set; }

    public string DisplayProvider => string.IsNullOrWhiteSpace(Provider) ? "Local" : Provider;
    public string PlaytimeDisplay => TotalPlaytime.TotalMinutes < 1
        ? "Not played yet"
        : TotalPlaytime.TotalHours >= 1
            ? $"{Math.Floor(TotalPlaytime.TotalHours):0}h {TotalPlaytime.Minutes}m"
            : $"{TotalPlaytime.TotalMinutes:0}m";
    public string LastPlayedDisplay => LastPlayed is null ? "Never played" : LastPlayed.Value.LocalDateTime.ToString("MMM d, yyyy");
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string CategoryLabel => Category switch
    {
        LibraryCategory.DevelopmentTool => "Development",
        LibraryCategory.MediaSoftware => "Media",
        _ => Category.ToString()
    };
}
