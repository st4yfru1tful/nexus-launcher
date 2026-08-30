using System.Collections.ObjectModel;
using System.Windows;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class StoreViewModel : PageViewModel
{
    private readonly WingetStoreService _storeService;
    private string _query = string.Empty;
    private string _status = "Search the WinGet community repository for legitimate software.";
    private bool _isSearching;
    private StorePackage? _selectedPackage;
    private bool _isWingetAvailable;

    public StoreViewModel(WingetStoreService storeService)
        : base("Store", "Discover and install software from verified package sources")
    {
        _storeService = storeService;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsSearching && !string.IsNullOrWhiteSpace(Query));
        InstallCommand = new RelayCommand(InstallSelected, () => SelectedPackage is not null && IsWingetAvailable);
    }

    public ObservableCollection<StorePackage> Packages { get; } = [];
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand InstallCommand { get; }
    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) SearchCommand.RaiseCanExecuteChanged(); }
    }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsSearching
    {
        get => _isSearching;
        set { if (SetProperty(ref _isSearching, value)) SearchCommand.RaiseCanExecuteChanged(); }
    }
    public bool IsWingetAvailable
    {
        get => _isWingetAvailable;
        private set { if (SetProperty(ref _isWingetAvailable, value)) InstallCommand.RaiseCanExecuteChanged(); }
    }
    public StorePackage? SelectedPackage
    {
        get => _selectedPackage;
        set { if (SetProperty(ref _selectedPackage, value)) InstallCommand.RaiseCanExecuteChanged(); }
    }

    public async Task InitializeAsync()
    {
        IsWingetAvailable = await _storeService.IsAvailableAsync();
        Status = IsWingetAvailable
            ? "Search WinGet for legitimate software. Nexus opens WinGet only after you choose Install."
            : "WinGet is not available on this Windows installation. Store search is disabled.";
    }

    private async Task SearchAsync()
    {
        IsSearching = true;
        Status = $"Searching WinGet for “{Query}”…";
        try
        {
            var results = await _storeService.SearchAsync(Query);
            Packages.Clear();
            foreach (var item in results) Packages.Add(item);
            Status = results.Count == 0
                ? "No packages matched. Try a more specific name or check that WinGet sources are available."
                : $"Found {results.Count} package{(results.Count == 1 ? string.Empty : "s")} from WinGet.";
        }
        catch (Exception exception)
        {
            Status = "Store search could not complete.";
            MessageBox.Show(exception.Message, "WinGet search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void InstallSelected()
    {
        if (SelectedPackage is null) return;
        var confirmation = MessageBox.Show(
            $"Nexus will start Windows Package Manager to install:\n\n{SelectedPackage.Name}\n{SelectedPackage.Id}\n\nContinue?",
            "Install package",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            _storeService.StartInstall(SelectedPackage);
            Status = $"WinGet installation started for {SelectedPackage.Name}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not start installation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
