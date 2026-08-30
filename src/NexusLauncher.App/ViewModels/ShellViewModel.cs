using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly LibraryRepository _libraryRepository = new();
    private readonly ExecutableInspector _inspector = new();
    private readonly AppSettings _settings = new();
    private readonly HomeViewModel _home;
    private readonly LibraryViewModel _library;
    private readonly StoreViewModel _store;
    private readonly ModsViewModel _mods;
    private readonly CollectionsViewModel _collections;
    private readonly CloudViewModel _cloud;
    private readonly SettingsViewModel _settingsPage;
    private readonly InfoPageViewModel _downloads;
    private readonly OllamaLocalMetadataProvider _localAiProvider;
    private readonly AiMetadataProviderRouter _aiProviderRouter;
    private object? _currentPage;
    private NavigationItem? _selectedNavigation;
    private string _globalStatus = "Starting Nexus…";
    private bool _isScanning;
    private bool _isCommandPaletteOpen;
    private string _commandQuery = string.Empty;
    private bool _isOnboardingOpen;
    private int _onboardingStep = 1;

    public ShellViewModel()
    {
        var discovery = new DiscoveryService(_inspector);
        var libraryService = new LibraryService(_libraryRepository, discovery);
        var aiOAuthClient = new NexusAiGatewayOAuthClient();
        _localAiProvider = new OllamaLocalMetadataProvider();
        _aiProviderRouter = new AiMetadataProviderRouter(
            _settings,
            _localAiProvider,
            new NexusAiGatewayClient(aiOAuthClient));
        var aiMetadataCoordinator = new AiMetadataCoordinator(
            _settings,
            _settingsService,
            _aiProviderRouter);
        LibraryItems = [];
        _home = new HomeViewModel(LibraryItems);
        _library = new LibraryViewModel(LibraryItems, libraryService, _settings, _settingsService, aiMetadataCoordinator);
        _store = new StoreViewModel();
        _mods = new ModsViewModel(LibraryItems);
        _collections = new CollectionsViewModel(LibraryItems);
        _cloud = new CloudViewModel(new BackupService());
        _settingsPage = new SettingsViewModel(
            _settingsService,
            _settings,
            ApplyTheme,
            aiOAuthClient,
            _aiProviderRouter,
            _library.RefreshAiAvailability);
        aiMetadataCoordinator.UsageChanged += _settingsPage.RefreshAiUsage;
        _downloads = new InfoPageViewModel(
            "Downloads",
            "Installation and update activity stays with its trusted source",
            "Nexus does not silently run downloads",
            "When you choose Install in Store, Nexus starts Windows Package Manager in its own visible window. Use the Library rescan after it finishes to find the new application. Game results open the official Steam page; Steam remains responsible for accounts, ownership, age checks, and downloads.",
            "\uE896");

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        OpenCommandPaletteCommand = new RelayCommand(OpenCommandPalette);
        CloseCommandPaletteCommand = new RelayCommand(() => IsCommandPaletteOpen = false);
        FinishOnboardingCommand = new AsyncRelayCommand(FinishOnboardingAsync);
        NextOnboardingCommand = new RelayCommand(() => OnboardingStep = Math.Min(3, OnboardingStep + 1));
        PreviousOnboardingCommand = new RelayCommand(() => OnboardingStep = Math.Max(1, OnboardingStep - 1));

        PaletteCommands =
        [
            new PaletteCommand("Rescan library", "Check Steam, installed applications, and Start menu shortcuts", ScanCommand),
            new PaletteCommand("Add an executable", "Add a specific .exe to Nexus", _library.AddExecutableCommand),
            new PaletteCommand("Open Store", "Search Steam games and WinGet software", new RelayCommand(() => NavigateTo("Store"))),
            new PaletteCommand("Open Settings", "Change scanning and privacy options", new RelayCommand(() => NavigateTo("Settings"))),
            new PaletteCommand("Show favorites", "Open the local Favorites collection", new RelayCommand(() => NavigateTo("Collections")))
        ];
        FilteredPaletteCommands = CollectionViewSource.GetDefaultView(PaletteCommands);
        FilteredPaletteCommands.Filter = command => command is PaletteCommand item &&
            (string.IsNullOrWhiteSpace(CommandQuery) || item.Name.Contains(CommandQuery, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(CommandQuery, StringComparison.OrdinalIgnoreCase));

        Navigation =
        [
            new NavigationItem("Home", "\uE80F", _home),
            new NavigationItem("Library", "\uE8F1", _library),
            new NavigationItem("Games", "\uE7FC", _library),
            new NavigationItem("Applications", "\uE71D", _library),
            new NavigationItem("Store", "\uE719", _store),
            new NavigationItem("Mods", "\uE74C", _mods),
            new NavigationItem("Downloads", "\uE896", _downloads),
            new NavigationItem("Collections", "\uE734", _collections),
            new NavigationItem("Cloud", "\uE753", _cloud),
            new NavigationItem("Settings", "\uE713", _settingsPage)
        ];
        SelectedNavigation = Navigation[0];
    }

    public ObservableCollection<LibraryItem> LibraryItems { get; }
    public IReadOnlyList<NavigationItem> Navigation { get; }
    public IReadOnlyList<PaletteCommand> PaletteCommands { get; }
    public ICollectionView FilteredPaletteCommands { get; }
    public ICommand ScanCommand { get; }
    public ICommand OpenCommandPaletteCommand { get; }
    public ICommand CloseCommandPaletteCommand { get; }
    public ICommand FinishOnboardingCommand { get; }
    public ICommand NextOnboardingCommand { get; }
    public ICommand PreviousOnboardingCommand { get; }
    public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public NavigationItem? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (!SetProperty(ref _selectedNavigation, value) || value is null) return;
            Navigate(value);
        }
    }
    public string GlobalStatus { get => _globalStatus; private set => SetProperty(ref _globalStatus, value); }
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value)) ((AsyncRelayCommand)ScanCommand).RaiseCanExecuteChanged();
        }
    }
    public bool IsCommandPaletteOpen { get => _isCommandPaletteOpen; set => SetProperty(ref _isCommandPaletteOpen, value); }
    public string CommandQuery
    {
        get => _commandQuery;
        set
        {
            if (SetProperty(ref _commandQuery, value)) FilteredPaletteCommands.Refresh();
        }
    }
    public bool IsOnboardingOpen { get => _isOnboardingOpen; private set => SetProperty(ref _isOnboardingOpen, value); }
    public int OnboardingStep
    {
        get => _onboardingStep;
        set
        {
            if (SetProperty(ref _onboardingStep, value))
            {
                OnPropertyChanged(nameof(OnboardingTitle));
                OnPropertyChanged(nameof(OnboardingBody));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanFinishOnboarding));
            }
        }
    }
    public bool CanGoBack => OnboardingStep > 1;
    public bool CanGoNext => OnboardingStep < 3;
    public bool CanFinishOnboarding => OnboardingStep == 3;
    public string OnboardingTitle => OnboardingStep switch { 1 => "Welcome to Nexus", 2 => "Find your games and apps", _ => "Your library stays yours" };
    public string OnboardingBody => OnboardingStep switch
    {
        1 => "Nexus is local-first. It never needs an account to catalog or launch what is already on this PC.",
        2 => "Nexus can look at Steam manifests, registered Windows applications, and Start menu shortcuts. It does not crawl every drive or upload executable files.",
        _ => "Store searches either hand off to Steam or start Windows Package Manager after you explicitly choose an action. Backups stay local. Optional metadata intelligence can run on-device, or use Nexus Cloud only when you select and connect it."
    };

    public async Task InitializeAsync()
    {
        var saved = await _settingsService.LoadAsync();
        CopySettings(saved, _settings);
        ApplyTheme(_settings.Theme);
        _library.RefreshAiAvailability();
        await _settingsPage.InitializeAsync();
        var items = await _libraryRepository.LoadAsync();
        foreach (var item in items) LibraryItems.Add(item);
        _home.Refresh();
        await _store.InitializeAsync();
        GlobalStatus = LibraryItems.Count == 0 ? "No library items yet — begin a scan or add an executable." : $"Loaded {LibraryItems.Count} local library item{(LibraryItems.Count == 1 ? string.Empty : "s")}.";
        IsOnboardingOpen = !_settings.HasCompletedOnboarding;
        OnPropertyChanged(nameof(OnboardingTitle));
        OnPropertyChanged(nameof(OnboardingBody));
    }

    public void NavigateTo(string name)
    {
        var navigation = Navigation.FirstOrDefault(item => item.Title == name);
        if (navigation is not null) SelectedNavigation = navigation;
    }

    public void BeginShutdown() => _localAiProvider.BeginShutdown();

    public async ValueTask DisposeAsync()
    {
        BeginShutdown();
        await _localAiProvider.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        _library.IsBusy = true;
        GlobalStatus = "Scanning your local sources…";
        var progress = new Progress<string>(message =>
        {
            GlobalStatus = message;
            _home.ScanStatus = message;
            _library.Status = message;
        });
        try
        {
            var result = await new LibraryService(_libraryRepository, new DiscoveryService(_inspector))
                .ScanAndMergeAsync(LibraryItems, _settings, progress);
            _home.Refresh();
            GlobalStatus = result.ItemsAdded == 0
                ? $"Scan complete — {result.ItemsFound} sources checked; no new items found."
                : $"Scan complete — added {result.ItemsAdded} item{(result.ItemsAdded == 1 ? string.Empty : "s")}.";
            if (result.Issues.Count > 0)
            {
                var diagnostics = string.Join("; ", result.Issues.Take(2).Select(issue => $"{FormatProviderName(issue.ProviderId)}: {issue.Message}"));
                var remainder = result.Issues.Count > 2 ? $" (+{result.Issues.Count - 2} more)" : string.Empty;
                GlobalStatus = $"{GlobalStatus} {result.Issues.Count} source warning{(result.Issues.Count == 1 ? string.Empty : "s")}: {diagnostics}{remainder}";
            }
            _home.ScanStatus = GlobalStatus;
            _library.Status = GlobalStatus;
        }
        catch (Exception exception)
        {
            GlobalStatus = "Library scan could not finish.";
            MessageBox.Show(exception.Message, "Library scan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsScanning = false;
            _library.IsBusy = false;
        }
    }

    private void Navigate(NavigationItem item)
    {
        if (item.Title == "Games") _library.SelectedCategory = "Games";
        if (item.Title == "Applications") _library.SelectedCategory = "Applications";
        if (item.Title == "Library") _library.SelectedCategory = "All items";
        CurrentPage = item.Target;
        IsCommandPaletteOpen = false;
    }

    private void OpenCommandPalette()
    {
        if (IsOnboardingOpen) return;
        CommandQuery = string.Empty;
        IsCommandPaletteOpen = true;
    }

    private async Task FinishOnboardingAsync()
    {
        _settings.HasCompletedOnboarding = true;
        await _settingsService.SaveAsync(_settings);
        IsOnboardingOpen = false;
        if (LibraryItems.Count == 0) await ScanAsync();
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.HasCompletedOnboarding = source.HasCompletedOnboarding;
        target.Theme = source.Theme;
        target.EnableAnimations = source.EnableAnimations;
        target.IncludeInstalledApplications = source.IncludeInstalledApplications;
        target.IncludeStartMenuShortcuts = source.IncludeStartMenuShortcuts;
        target.EnableAiMetadata = source.EnableAiMetadata;
        target.AiProvider = Enum.IsDefined(source.AiProvider) ? source.AiProvider : AiProviderMode.OnDevice;
        target.AiMonthlyRequestLimit = source.AiMonthlyRequestLimit;
        target.AiUsageMonth = source.AiUsageMonth;
        target.AiRequestsThisMonth = source.AiRequestsThisMonth;
        target.ScanFolders = source.ScanFolders;
        target.IgnoredPaths = source.IgnoredPaths;
        target.IgnoredIdentities = source.IgnoredIdentities;
    }

    private static string FormatProviderName(string providerId) => providerId switch
    {
        "steam" => "Steam",
        "windows-registry" => "Windows registry",
        "start-menu" => "Start menu",
        _ => providerId
    };

    private static void ApplyTheme(AppTheme theme)
    {
        var dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.System => UsesSystemDarkTheme(),
            _ => true
        };
        SetBrush("NexusBackground", dark ? "#0D111A" : "#F4F6FA");
        SetBrush("NexusPanel", dark ? "#171C28" : "#FFFFFF");
        SetBrush("NexusPanelRaised", dark ? "#202737" : "#E8EDF7");
        SetBrush("NexusText", dark ? "#F7F9FC" : "#172033");
        SetBrush("NexusMutedText", dark ? "#A9B4C8" : "#536176");
        SetBrush("NexusAccent", "#8A7CFF");
        SetBrush("NexusAccentHover", "#A69CFF");
        SetBrush("NexusAccentText", dark ? "#A69CFF" : "#5B4BC5");
        SetBrush("NexusFocus", dark ? "#F7F9FC" : "#172033");
        SetBrush("NexusInput", dark ? "#111722" : "#FFFFFF");
        SetBrush("NexusBorder", dark ? "#354057" : "#C4CDDB");
        SetBrush("NexusBorderStrong", dark ? "#526583" : "#909CB0");
        SetBrush("NexusHover", dark ? "#222B3D" : "#E9EEF7");
        SetBrush("NexusSelected", dark ? "#332C52" : "#E6E2FF");
        SetBrush("NexusOnAccent", "#0B0E18");
        SetBrush("NexusDangerSurface", dark ? "#3A1C29" : "#FDEBF0");
        SetBrush("NexusDangerBorder", dark ? "#98566E" : "#C66D89");
        SetBrush("NexusScrim", dark ? "#DD0A0D14" : "#B82B3242");
        SetBrush("NexusSidebar", dark ? "#141923" : "#FFFFFF");
        SetBrush("NexusHeader", dark ? "#E0141923" : "#EDF4F6FA");
    }

    private static void SetBrush(string key, string color)
    {
        // Brushes referenced from XAML styles can be frozen by WPF. Replacing the
        // resource is safe in either state; mutating a frozen shared brush is not.
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private static bool UsesSystemDarkTheme()
    {
        var value = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            0);
        return value is not int light || light == 0;
    }
}
