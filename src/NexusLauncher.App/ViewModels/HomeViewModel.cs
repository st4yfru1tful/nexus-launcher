using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.ViewModels;

public sealed class HomeViewModel : PageViewModel
{
    private readonly ObservableCollection<LibraryItem> _library;
    private string _scanStatus = "Your local library is ready.";

    public HomeViewModel(ObservableCollection<LibraryItem> library)
        : base("Home", "Your library, at a glance")
    {
        _library = library;
        _library.CollectionChanged += OnLibraryChanged;
        foreach (var item in _library) item.PropertyChanged += OnLibraryItemPropertyChanged;
    }

    public IEnumerable<LibraryItem> ContinuePlaying => _library
        .Where(item => item.LastPlayed is not null && !item.IsHidden)
        .OrderByDescending(item => item.LastPlayed)
        .Take(6);
    public IEnumerable<LibraryItem> Favorites => _library.Where(item => item.IsFavorite && !item.IsHidden).Take(6);
    public int GameCount => _library.Count(item => item.Category == LibraryCategory.Game && !item.IsHidden);
    public int AppCount => _library.Count(item => item.Category != LibraryCategory.Game && !item.IsHidden);
    public int FavoriteCount => _library.Count(item => item.IsFavorite && !item.IsHidden);
    public bool HasRecentItems => ContinuePlaying.Any();
    public bool HasFavorites => Favorites.Any();
    public string ScanStatus { get => _scanStatus; set => SetProperty(ref _scanStatus, value); }

    public void Refresh()
    {
        OnPropertyChanged(nameof(ContinuePlaying));
        OnPropertyChanged(nameof(Favorites));
        OnPropertyChanged(nameof(GameCount));
        OnPropertyChanged(nameof(AppCount));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(HasRecentItems));
        OnPropertyChanged(nameof(HasFavorites));
    }

    private void OnLibraryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (LibraryItem item in e.OldItems) item.PropertyChanged -= OnLibraryItemPropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (LibraryItem item in e.NewItems) item.PropertyChanged += OnLibraryItemPropertyChanged;
        }
        Refresh();
    }

    private void OnLibraryItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
}
