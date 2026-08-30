using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Action<AppTheme> _applyTheme;
    private readonly NexusAiGatewayOAuthClient _aiOAuthClient;
    private readonly Action? _onAiSettingsChanged;
    private readonly string _dataStorageDescription = NexusPaths.IsPortableMode
        ? "Portable mode is active. Your library, settings, cache, and diagnostics stay in NexusLauncherData next to this copy of Nexus."
        : "Your library and settings are stored under LocalAppData. No account is required.";
    private string _status = "Changes are saved locally on this PC.";
    private string _aiConnectionStatus = "Checking Nexus AI availability…";
    private bool _isAiConnected;

    public SettingsViewModel(SettingsService settingsService, AppSettings settings, Action<AppTheme> applyTheme)
        : this(settingsService, settings, applyTheme, null, null)
    {
    }

    public SettingsViewModel(
        SettingsService settingsService,
        AppSettings settings,
        Action<AppTheme> applyTheme,
        NexusAiGatewayOAuthClient? aiOAuthClient,
        Action? onAiSettingsChanged)
        : base("Settings", "Control what Nexus scans and what it keeps local")
    {
        _settingsService = settingsService;
        _settings = settings;
        _applyTheme = applyTheme;
        _aiOAuthClient = aiOAuthClient ?? new NexusAiGatewayOAuthClient();
        _onAiSettingsChanged = onAiSettingsChanged;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        OpenDataCommand = new RelayCommand(OpenDataFolder);
        AddScanFolderCommand = new RelayCommand(AddScanFolder);
        RemoveScanFolderCommand = new AsyncRelayCommand(RemoveScanFolderAsync, () => SelectedScanFolder is not null);
        ConnectAiCommand = new AsyncRelayCommand(ConnectAiAsync, () => IsAiGatewayConfigured && !IsAiConnected);
        DisconnectAiCommand = new AsyncRelayCommand(DisconnectAiAsync, () => IsAiGatewayConfigured && IsAiConnected);
    }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<int> AiRequestLimits { get; } = [10, 25, 50, 100];
    public List<string> ScanFolders => _settings.ScanFolders;
    public string DataStorageDescription => _dataStorageDescription;
    public bool IsAiGatewayConfigured => _aiOAuthClient.IsConfigured;
    public string AiConnectionStatus { get => _aiConnectionStatus; private set => SetProperty(ref _aiConnectionStatus, value); }
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
    public string AiUsageDescription => $"{_settings.AiRequestsThisMonth} of {_settings.AiMonthlyRequestLimit} local AI requests used this month.";
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
                ? "AI metadata suggestions are enabled locally. Nexus will wait for an explicit request and a secure Nexus AI connection."
                : "AI metadata suggestions are disabled. Nexus will not send metadata to the AI gateway.";
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
        if (!IsAiGatewayConfigured)
        {
            IsAiConnected = false;
            AiConnectionStatus = _aiOAuthClient.AvailabilityMessage;
            return;
        }

        try
        {
            IsAiConnected = await _aiOAuthClient.HasSessionAsync();
            AiConnectionStatus = IsAiConnected
                ? "A usable Nexus AI session is ready for this Windows user."
                : "Nexus AI is configured but not connected. Sign in before requesting suggestions.";
        }
        catch (Exception)
        {
            IsAiConnected = false;
            AiConnectionStatus = "Nexus AI session status could not be read. It remains disconnected until you sign in again.";
        }
    }
}
