using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    public sealed record AiProviderChoice(AiProviderMode Mode, string Name, string Summary);

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Action<AppTheme> _applyTheme;
    private readonly NexusAiGatewayOAuthClient _aiOAuthClient;
    private readonly IAiMetadataProvider _aiProvider;
    private readonly Action? _onAiSettingsChanged;
    private readonly string _dataStorageDescription = NexusPaths.IsPortableMode
        ? "Portable mode is active. Your library, settings, cache, and diagnostics stay in NexusLauncherData next to this copy of Nexus."
        : "Your library and settings are stored under LocalAppData. No account is required.";
    private string _status = "Changes are saved locally on this PC.";
    private string _aiConnectionStatus = "Checking Nexus AI availability…";
    private bool _isAiConnected;

    public SettingsViewModel(
        SettingsService settingsService,
        AppSettings settings,
        Action<AppTheme> applyTheme,
        NexusAiGatewayOAuthClient aiOAuthClient,
        IAiMetadataProvider aiProvider,
        Action? onAiSettingsChanged)
        : base("Settings", "Control what Nexus scans and what it keeps local")
    {
        _settingsService = settingsService;
        _settings = settings;
        _applyTheme = applyTheme;
        _aiOAuthClient = aiOAuthClient ?? throw new ArgumentNullException(nameof(aiOAuthClient));
        _aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
        _onAiSettingsChanged = onAiSettingsChanged;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        OpenDataCommand = new RelayCommand(OpenDataFolder);
        AddScanFolderCommand = new RelayCommand(AddScanFolder);
        RemoveScanFolderCommand = new AsyncRelayCommand(RemoveScanFolderAsync, () => SelectedScanFolder is not null);
        ConnectAiCommand = new AsyncRelayCommand(ConnectAiAsync, () => IsCloudAiProvider && IsAiGatewayConfigured && !IsAiConnected);
        DisconnectAiCommand = new AsyncRelayCommand(DisconnectAiAsync, () => IsCloudAiProvider && IsAiGatewayConfigured && IsAiConnected);
        RefreshAiStatusCommand = new AsyncRelayCommand(RefreshAiConnectionStateAsync);
        OpenLocalAiSetupCommand = new RelayCommand(OpenLocalAiSetup);
    }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<AiProviderChoice> AiProviders { get; } =
    [
        new(AiProviderMode.OnDevice, "On-device AI", "Private processing with a downloaded Ollama text-generation model"),
        new(AiProviderMode.NexusCloud, "Nexus Cloud", "Optional OAuth gateway when a trusted Nexus service is configured")
    ];
    public IReadOnlyList<int> AiRequestLimits { get; } = [10, 25, 50, 100];
    public List<string> ScanFolders => _settings.ScanFolders;
    public string DataStorageDescription => _dataStorageDescription;
    public bool IsAiGatewayConfigured => _aiOAuthClient.IsConfigured;
    public string AiConnectionStatus { get => _aiConnectionStatus; private set => SetProperty(ref _aiConnectionStatus, value); }
    public AiProviderChoice SelectedAiProvider
    {
        get => AiProviders.FirstOrDefault(choice => choice.Mode == _settings.AiProvider) ?? AiProviders[0];
        set
        {
            if (value is null || _settings.AiProvider == value.Mode) return;
            _settings.AiProvider = value.Mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalAiProvider));
            OnPropertyChanged(nameof(IsCloudAiProvider));
            OnPropertyChanged(nameof(AiUsageDescription));
            RefreshAiCommands();
            _onAiSettingsChanged?.Invoke();
            Status = $"Selected {value.Name}. Save settings to keep this choice.";
            RefreshAiStatusCommand.Execute(null);
        }
    }
    public bool IsLocalAiProvider => _settings.AiProvider == AiProviderMode.OnDevice;
    public bool IsCloudAiProvider => _settings.AiProvider == AiProviderMode.NexusCloud;
    public bool IsAiConnected
    {
        get => _isAiConnected;
        private set
        {
            if (!SetProperty(ref _isAiConnected, value)) return;
            ConnectAiCommand.RaiseCanExecuteChanged();
            DisconnectAiCommand.RaiseCanExecuteChanged();
        }
    }
    public string AiUsageDescription => IsLocalAiProvider
        ? "On-device requests stay on this PC and have no Nexus quota."
        : $"{_settings.AiRequestsThisMonth} of {_settings.AiMonthlyRequestLimit} Nexus Cloud requests used this month.";
    private string? _selectedScanFolder;
    public string? SelectedScanFolder { get => _selectedScanFolder; set { if (SetProperty(ref _selectedScanFolder, value)) RemoveScanFolderCommand.RaiseCanExecuteChanged(); } }
    public bool IncludeInstalledApplications { get => _settings.IncludeInstalledApplications; set { _settings.IncludeInstalledApplications = value; OnPropertyChanged(); } }
    public bool IncludeStartMenuShortcuts { get => _settings.IncludeStartMenuShortcuts; set { _settings.IncludeStartMenuShortcuts = value; OnPropertyChanged(); } }
    public bool EnableAiMetadata
    {
        get => _settings.EnableAiMetadata;
        set
        {
            if (_settings.EnableAiMetadata == value) return;
            _settings.EnableAiMetadata = value;
            OnPropertyChanged();
            _onAiSettingsChanged?.Invoke();
            Status = value
                ? "Metadata intelligence is enabled. Nexus still waits for you to request and approve each suggestion."
                : "Metadata intelligence is disabled. No AI provider will receive a request.";
            RefreshAiStatusCommand.Execute(null);
        }
    }
    public int AiMonthlyRequestLimit
    {
        get => _settings.AiMonthlyRequestLimit;
        set
        {
            if (_settings.AiMonthlyRequestLimit == value) return;
            _settings.AiMonthlyRequestLimit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AiUsageDescription));
        }
    }
    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            _settings.Theme = value;
            _applyTheme(value);
            OnPropertyChanged();
        }
    }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public RelayCommand OpenDataCommand { get; }
    public RelayCommand AddScanFolderCommand { get; }
    public AsyncRelayCommand RemoveScanFolderCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ConnectAiCommand { get; }
    public AsyncRelayCommand DisconnectAiCommand { get; }
    public AsyncRelayCommand RefreshAiStatusCommand { get; }
    public RelayCommand OpenLocalAiSetupCommand { get; }

    public async Task InitializeAsync()
    {
        await RefreshAiConnectionStateAsync();
    }

    public void RefreshAiUsage() => OnPropertyChanged(nameof(AiUsageDescription));

    private async Task SaveAsync()
    {
        await _settingsService.SaveAsync(_settings);
        Status = "Settings saved.";
    }

    private void AddScanFolder()
    {
        var picker = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = "Choose an application in the folder to scan" };
        if (picker.ShowDialog() != true) return;
        var folder = Path.GetDirectoryName(picker.FileName);
        if (string.IsNullOrWhiteSpace(folder) || _settings.ScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        _settings.ScanFolders.Add(folder);
        OnPropertyChanged(nameof(ScanFolders));
        Status = $"Added {folder}. Save settings, then rescan the library.";
    }

    private async Task RemoveScanFolderAsync()
    {
        if (SelectedScanFolder is null) return;
        _settings.ScanFolders.Remove(SelectedScanFolder);
        SelectedScanFolder = null;
        await _settingsService.SaveAsync(_settings);
        OnPropertyChanged(nameof(ScanFolders));
        Status = "Scan folder removed.";
    }

    private static void OpenDataFolder()
    {
        NexusPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo(NexusPaths.Root) { UseShellExecute = true });
    }

    private void OpenLocalAiSetup()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://ollama.com/download/windows") { UseShellExecute = true });
            Status = "Opened the official Ollama for Windows setup page.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status = "The Ollama setup page could not be opened. Visit ollama.com/download/windows in your browser.";
        }
    }

    private async Task ConnectAiAsync()
    {
        try
        {
            var result = await _aiOAuthClient.ConnectAsync();
            await RefreshAiConnectionStateAsync();
            if (!result.Succeeded) AiConnectionStatus = result.Message;
            Status = result.Message;
            _onAiSettingsChanged?.Invoke();
        }
        catch (Exception)
        {
            AiConnectionStatus = "Nexus AI could not start sign-in. No library metadata was sent.";
            Status = AiConnectionStatus;
        }
    }

    private async Task DisconnectAiAsync()
    {
        try
        {
            await _aiOAuthClient.DisconnectAsync();
            await RefreshAiConnectionStateAsync();
            Status = AiConnectionStatus;
            _onAiSettingsChanged?.Invoke();
        }
        catch (Exception)
        {
            AiConnectionStatus = "Nexus AI could not remove the local session. Close Nexus and try again.";
            Status = AiConnectionStatus;
        }
    }

    private async Task RefreshAiConnectionStateAsync()
    {
        if (!_settings.EnableAiMetadata)
        {
            IsAiConnected = false;
            AiConnectionStatus = IsLocalAiProvider
                ? "On-device AI support is included. Enable metadata intelligence to check for an installed Ollama runtime and compatible text-generation model."
                : "Nexus Cloud is optional. Enable metadata intelligence to check its connection without sending library metadata.";
            return;
        }

        try
        {
            var availability = await _aiProvider.GetAvailabilityAsync();
            IsAiConnected = availability.IsReady;
            AiConnectionStatus = availability.State switch
            {
                AiMetadataProviderState.RuntimeUnavailable when IsLocalAiProvider =>
                    "On-device AI is built in, but Ollama for Windows was not found. Install it from the official page, then refresh.",
                AiMetadataProviderState.NoLocalModel when IsLocalAiProvider =>
                    "Ollama is available, but no downloaded text-generation model is ready. Embedding-only and cloud models are not used, and Nexus never downloads models automatically.",
                _ => availability.Message
            };
        }
        catch (Exception)
        {
            IsAiConnected = false;
            AiConnectionStatus = IsLocalAiProvider
                ? "On-device AI status could not be checked. No library metadata left this PC."
                : "Nexus Cloud status could not be checked. No library metadata was sent.";
        }

        RefreshAiCommands();
    }

    private void RefreshAiCommands()
    {
        ConnectAiCommand.RaiseCanExecuteChanged();
        DisconnectAiCommand.RaiseCanExecuteChanged();
    }
}
