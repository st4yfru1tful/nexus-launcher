using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.ViewModels;

public sealed class CollectionsViewModel : PageViewModel
{
    private readonly ObservableCollection<LibraryItem> _library;
    private string _selectedCollection = "Favorites";

    public CollectionsViewModel(ObservableCollection<LibraryItem> library)
        : base("Collections", "Useful local views of the library you already own")
    {
        _library = library;
        // Use a private view because the Library page applies a different filter to
        // this shared source collection.
        Items = new ListCollectionView(_library);
        Items.Filter = Filter;
        _library.CollectionChanged += OnChanged;
        foreach (var item in _library) item.PropertyChanged += OnItemPropertyChanged;
    }

    public IReadOnlyList<string> Collections { get; } = ["Favorites", "Recently Played", "Never Played", "Steam", "Manual Additions"];
    public ICollectionView Items { get; }
    public string SelectedCollection
    {
        get => _selectedCollection;
        set { if (SetProperty(ref _selectedCollection, value)) Items.Refresh(); }
    }

    private bool Filter(object value)
    {
        if (value is not LibraryItem item || item.IsHidden) return false;
        return SelectedCollection switch
        {
            "Favorites" => item.IsFavorite,
            "Recently Played" => item.LastPlayed is not null,
            "Never Played" => item.LastPlayed is null,
            "Steam" => string.Equals(item.Provider, "Steam", StringComparison.OrdinalIgnoreCase),
            "Manual Additions" => item.IsManual,
            _ => true
        };
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (LibraryItem item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (LibraryItem item in e.NewItems) item.PropertyChanged += OnItemPropertyChanged;
        }
        Items.Refresh();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => Items.Refresh();
}
