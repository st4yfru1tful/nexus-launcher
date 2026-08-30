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
    private string _status = "Changes are saved locally on this PC.";

    public SettingsViewModel(SettingsService settingsService, AppSettings settings, Action<AppTheme> applyTheme)
        : base("Settings", "Control what Nexus scans and what it keeps local")
    {
        _settingsService = settingsService;
        _settings = settings;
        _applyTheme = applyTheme;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        OpenDataCommand = new RelayCommand(OpenDataFolder);
        AddScanFolderCommand = new RelayCommand(AddScanFolder);
        RemoveScanFolderCommand = new AsyncRelayCommand(RemoveScanFolderAsync, () => SelectedScanFolder is not null);
    }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public List<string> ScanFolders => _settings.ScanFolders;
    private string? _selectedScanFolder;
    public string? SelectedScanFolder { get => _selectedScanFolder; set { if (SetProperty(ref _selectedScanFolder, value)) RemoveScanFolderCommand.RaiseCanExecuteChanged(); } }
    public bool IncludeInstalledApplications { get => _settings.IncludeInstalledApplications; set { _settings.IncludeInstalledApplications = value; OnPropertyChanged(); } }
    public bool IncludeStartMenuShortcuts { get => _settings.IncludeStartMenuShortcuts; set { _settings.IncludeStartMenuShortcuts = value; OnPropertyChanged(); } }
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
}
