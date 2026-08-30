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
    public string? InstallPath
    {
        get => _installPath;
        set
        {
            if (SetProperty(ref _installPath, value)) OnPropertyChanged(nameof(InstallLocationDisplay));
        }
    }
    public string? Provider
    {
        get => _provider;
        set
        {
            if (SetProperty(ref _provider, value)) OnPropertyChanged(nameof(DisplayProvider));
        }
    }
    public string? Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value)) OnPropertyChanged(nameof(DescriptionDisplay));
        }
    }
    public string? Publisher
    {
        get => _publisher;
        set
        {
            if (SetProperty(ref _publisher, value)) OnPropertyChanged(nameof(PublisherDisplay));
        }
    }
    public string? Version
    {
        get => _version;
        set
        {
            if (SetProperty(ref _version, value)) OnPropertyChanged(nameof(VersionDisplay));
        }
    }
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
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value)) OnPropertyChanged(nameof(FavoriteActionLabel));
        }
    }
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (SetProperty(ref _isHidden, value)) OnPropertyChanged(nameof(VisibilityActionLabel));
        }
    }
    public bool IsManual { get => _isManual; set => SetProperty(ref _isManual, value); }
    public long? InstallSizeBytes { get => _installSizeBytes; set => SetProperty(ref _installSizeBytes, value); }

    [JsonIgnore]
    public string DisplayProvider => string.IsNullOrWhiteSpace(Provider) ? "Local" : Provider;
    [JsonIgnore]
    public string PublisherDisplay => string.IsNullOrWhiteSpace(Publisher) ? "Not provided" : Publisher;
    [JsonIgnore]
    public string VersionDisplay => string.IsNullOrWhiteSpace(Version) ? "Not provided" : Version;
    [JsonIgnore]
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "No description yet" : Description;
    [JsonIgnore]
    public string InstallLocationDisplay => string.IsNullOrWhiteSpace(InstallPath) ? "Not provided" : InstallPath;
    [JsonIgnore]
    public string FavoriteActionLabel => IsFavorite ? "Remove from favorites" : "Add to favorites";
    [JsonIgnore]
    public string VisibilityActionLabel => IsHidden ? "Show in library" : "Hide from library";
    /// <summary>
    /// Nexus records that a launch was requested, but deliberately does not
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
