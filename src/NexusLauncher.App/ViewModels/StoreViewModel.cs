using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class StoreViewModel : PageViewModel
{
    public const string GamesScope = "Games · Steam";
    public const string SoftwareScope = "Software · WinGet";

    private readonly SteamStoreService _steamStoreService;
    private string _query = string.Empty;
    private string _selectedScope = GamesScope;
    private string _status = "Search the Steam Store for games or WinGet for legitimate Windows software.";
    private bool _isSearching;
    private StorePackage? _selectedPackage;
    private bool _isWingetAvailable;
    private bool _hasResults;
    private CancellationTokenSource? _activeSearchCancellation;

    public StoreViewModel(SteamStoreService? steamStoreService = null)
        : base("Store", "Discover legitimate games and software from their trusted providers")
    {
        _steamStoreService = steamStoreService ?? new SteamStoreService();
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsSearching && !string.IsNullOrWhiteSpace(Query));
        PrimaryActionCommand = new RelayCommand(PerformPrimaryAction, CanPerformPrimaryAction);
    }

    public ObservableCollection<StorePackage> Packages { get; } = [];
    public IReadOnlyList<string> SearchScopes { get; } = [GamesScope, SoftwareScope];
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand PrimaryActionCommand { get; }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value)) SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedScope
    {
        get => _selectedScope;
        set
        {
            var normalized = string.Equals(value, SoftwareScope, StringComparison.Ordinal) ? SoftwareScope : GamesScope;
            if (!SetProperty(ref _selectedScope, normalized)) return;
            CancelActiveSearch();
            Packages.Clear();
            SelectedPackage = null;
            HasResults = false;
            OnPropertyChanged(nameof(IsGameSearch));
            OnPropertyChanged(nameof(SearchPlaceholder));
            OnPropertyChanged(nameof(SearchButtonText));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateBody));
            Status = IsGameSearch
                ? "Search Steam's public storefront catalog. Nexus opens the official Steam page only when you choose View in Steam."
                : IsWingetAvailable
                    ? "Search WinGet for legitimate Windows software. Nexus starts WinGet only after you choose Install."
                    : "WinGet is unavailable on this Windows installation. Game search is still available through Steam.";
        }
    }

    public bool IsGameSearch => string.Equals(SelectedScope, GamesScope, StringComparison.Ordinal);
    public string SearchPlaceholder => IsGameSearch ? "Search Steam games" : "Search Windows software";
    public string SearchButtonText => IsGameSearch ? "Search games" : "Search software";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value)) SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsWingetAvailable
    {
        get => _isWingetAvailable;
        private set
        {
            if (SetProperty(ref _isWingetAvailable, value))
            {
                PrimaryActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public StorePackage? SelectedPackage
    {
        get => _selectedPackage;
        set
        {
            if (!SetProperty(ref _selectedPackage, value)) return;
            OnPropertyChanged(nameof(PrimaryActionLabel));
            PrimaryActionCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasResults { get => _hasResults; private set => SetProperty(ref _hasResults, value); }
    public string PrimaryActionLabel => SelectedPackage?.Action == StorePackageAction.OpenExternalStore
        ? "View in Steam"
        : "Install selected";
    public string EmptyStateTitle => IsGameSearch ? "Find something new" : "Find trusted Windows software";
    public string EmptyStateBody => IsGameSearch
        ? "Search the Steam Store by title. Results are catalog listings only; Nexus never assumes ownership and opens Steam for the next step."
        : "Search the WinGet community repository. Nexus starts Windows Package Manager only after you explicitly choose Install.";

    public async Task InitializeAsync()
    {
        IsWingetAvailable = await WingetStoreService.IsAvailableAsync();
        Status = IsWingetAvailable
            ? "Search the Steam Store for games or WinGet for legitimate Windows software."
            : "Search Steam games now. WinGet is unavailable on this Windows installation.";
    }

    private async Task SearchAsync()
    {
        var scope = SelectedScope;
        var query = Query;
        using var cancellation = new CancellationTokenSource();
        var previousCancellation = _activeSearchCancellation;
        _activeSearchCancellation = cancellation;
        previousCancellation?.Cancel();

        IsSearching = true;
        Status = string.Equals(scope, GamesScope, StringComparison.Ordinal)
            ? $"Searching Steam for “{query}”…"
            : $"Searching WinGet for “{query}”…";
        try
        {
            var results = string.Equals(scope, GamesScope, StringComparison.Ordinal)
                ? await _steamStoreService.SearchAsync(query, GetCountryCode(), cancellation.Token)
                : await SearchWingetAsync(query, cancellation.Token);
            if (!IsCurrentSearch(cancellation, scope, query)) return;

            Packages.Clear();
            foreach (var item in results) Packages.Add(item);
            HasResults = Packages.Count > 0;
            SelectedPackage = Packages.FirstOrDefault();
            Status = results.Count == 0
                ? string.Equals(scope, GamesScope, StringComparison.Ordinal)
                    ? "No Steam catalog entries matched. Try a more specific title or check your connection."
                    : "No WinGet packages matched. Try a more specific name or check that WinGet sources are available."
                : string.Equals(scope, GamesScope, StringComparison.Ordinal)
                    ? $"Found {results.Count} Steam result{(results.Count == 1 ? string.Empty : "s")}. Select one to open its official store page."
                    : $"Found {results.Count} WinGet package{(results.Count == 1 ? string.Empty : "s")}.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer store scope or search superseded this request.
        }
        catch (Exception exception)
        {
            if (!IsCurrentSearch(cancellation, scope, query)) return;
            HasResults = Packages.Count > 0;
            Status = "Store search could not complete.";
            MessageBox.Show(
                string.Equals(scope, GamesScope, StringComparison.Ordinal)
                    ? "Steam search could not complete. Check your connection and try again."
                    : exception.Message,
                string.Equals(scope, GamesScope, StringComparison.Ordinal) ? "Steam search" : "WinGet search",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_activeSearchCancellation, cancellation))
            {
                _activeSearchCancellation = null;
                IsSearching = false;
            }
        }
    }

    private async Task<IReadOnlyList<StorePackage>> SearchWingetAsync(string query, CancellationToken cancellationToken)
    {
        if (!IsWingetAvailable)
        {
            throw new InvalidOperationException("WinGet is not available on this Windows installation.");
        }

        return await WingetStoreService.SearchAsync(query, cancellationToken);
    }

    private bool CanPerformPrimaryAction()
    {
        return SelectedPackage switch
        {
            { Action: StorePackageAction.InstallWithWinget } => IsWingetAvailable,
            { Action: StorePackageAction.OpenExternalStore, StoreUrl: not null } package => IsSafeSteamStoreUrl(package.StoreUrl),
            _ => false
        };
    }

    private void PerformPrimaryAction()
    {
        if (SelectedPackage is null) return;
        if (SelectedPackage.Action == StorePackageAction.OpenExternalStore)
        {
            OpenSelectedStorePage();
            return;
        }

        InstallSelectedPackage();
    }

    private void InstallSelectedPackage()
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
            WingetStoreService.StartInstall(SelectedPackage);
            Status = $"WinGet installation started for {SelectedPackage.Name}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not start installation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenSelectedStorePage()
    {
        if (SelectedPackage?.StoreUrl is not { } storeUrl || !IsSafeSteamStoreUrl(storeUrl)) return;
        var confirmation = MessageBox.Show(
            $"Nexus will open the official Steam Store page for:\n\n{SelectedPackage.Name}\n\nNexus does not authenticate with Steam, check ownership, purchase, or download games. Continue?",
            "View in Steam",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            Process.Start(new ProcessStartInfo(storeUrl) { UseShellExecute = true });
            Status = $"Opened the official Steam page for {SelectedPackage.Name}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not open Steam", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string GetCountryCode()
    {
        try
        {
            var code = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            return code.Length == 2 ? code : "us";
        }
        catch (CultureNotFoundException)
        {
            return "us";
        }
    }

    private bool IsCurrentSearch(CancellationTokenSource cancellation, string scope, string query) =>
        ReferenceEquals(_activeSearchCancellation, cancellation) &&
        string.Equals(SelectedScope, scope, StringComparison.Ordinal) &&
        string.Equals(Query, query, StringComparison.Ordinal);

    private void CancelActiveSearch()
    {
        var cancellation = _activeSearchCancellation;
        _activeSearchCancellation = null;
        cancellation?.Cancel();
        if (IsSearching) IsSearching = false;
    }

    private static bool IsSafeSteamStoreUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.Host, "store.steampowered.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/app/", StringComparison.Ordinal) &&
            long.TryParse(uri.Segments.ElementAtOrDefault(2)?.Trim('/'), NumberStyles.None, CultureInfo.InvariantCulture, out var appId) &&
            appId > 0;
    }
}
