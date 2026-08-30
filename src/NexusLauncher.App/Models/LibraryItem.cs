using NexusLauncher.App.Infrastructure;
using System.Text.Json.Serialization;

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

public sealed class LibraryItem : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private string _name = "Untitled item";
    private LibraryCategory _category = LibraryCategory.Unknown;
    private string? _executablePath;
    private string? _launchUri;
    private string? _launchArguments;
    private string? _workingDirectory;
    private string? _installPath;
    private string? _provider;
    private string? _description;
    private string? _publisher;
    private string? _version;
    private string? _iconPath;
    private List<string> _tags = [];
    private DateTimeOffset _dateDiscovered = DateTimeOffset.UtcNow;
    private DateTimeOffset? _lastPlayed;
    private bool _isFavorite;
    private bool _isHidden;
    private bool _isManual;
    private long? _installSizeBytes;

    public Guid Id { get => _id; init => _id = value; }
    public string Name
    {
        get => _name;
        set
        {
            var sanitized = string.IsNullOrWhiteSpace(value) ? "Untitled item" : value.Trim();
            if (SetProperty(ref _name, sanitized)) OnPropertyChanged(nameof(Initial));
        }
    }
    public LibraryCategory Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, value)) OnPropertyChanged(nameof(CategoryLabel));
        }
    }
    public string? ExecutablePath { get => _executablePath; set => SetProperty(ref _executablePath, value); }
    public string? LaunchUri { get => _launchUri; set => SetProperty(ref _launchUri, value); }
    public string? LaunchArguments { get => _launchArguments; set => SetProperty(ref _launchArguments, value); }
    public string? WorkingDirectory { get => _workingDirectory; set => SetProperty(ref _workingDirectory, value); }
    public string? InstallPath { get => _installPath; set => SetProperty(ref _installPath, value); }
    public string? Provider
    {
        get => _provider;
        set
        {
            if (SetProperty(ref _provider, value)) OnPropertyChanged(nameof(DisplayProvider));
        }
    }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string? Publisher { get => _publisher; set => SetProperty(ref _publisher, value); }
    public string? Version { get => _version; set => SetProperty(ref _version, value); }
    public string? IconPath { get => _iconPath; set => SetProperty(ref _iconPath, value); }
    public List<string> Tags { get => _tags; set => SetProperty(ref _tags, value?.OfType<string>().ToList() ?? []); }
    public DateTimeOffset DateDiscovered { get => _dateDiscovered; set => SetProperty(ref _dateDiscovered, value); }
    public DateTimeOffset? LastPlayed
    {
        get => _lastPlayed;
        set
        {
            if (SetProperty(ref _lastPlayed, value)) OnPropertyChanged(nameof(LaunchActivityDisplay));
        }
    }
    public bool IsFavorite { get => _isFavorite; set => SetProperty(ref _isFavorite, value); }
    public bool IsHidden { get => _isHidden; set => SetProperty(ref _isHidden, value); }
    public bool IsManual { get => _isManual; set => SetProperty(ref _isManual, value); }
    public long? InstallSizeBytes { get => _installSizeBytes; set => SetProperty(ref _installSizeBytes, value); }

    [JsonIgnore]
    public string DisplayProvider => string.IsNullOrWhiteSpace(Provider) ? "Local" : Provider;
    /// <summary>
    /// Nexus v0.1 records that a launch was requested, but deliberately does not
    /// estimate playtime across external launchers or provider URIs.
    /// </summary>
    [JsonIgnore]
    public string LaunchActivityDisplay => LastPlayed is null
        ? "Not launched yet"
        : $"Launched {LastPlayed.Value.LocalDateTime:MMM d, yyyy}";
    [JsonIgnore]
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
    [JsonIgnore]
    public string CategoryLabel => Category switch
    {
        LibraryCategory.DevelopmentTool => "Development",
        LibraryCategory.MediaSoftware => "Media",
        _ => Category.ToString()
    };
}
