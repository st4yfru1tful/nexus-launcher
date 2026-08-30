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
        Items = CollectionViewSource.GetDefaultView(_library);
        Items.Filter = Filter;
        _library.CollectionChanged += OnChanged;
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

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => Items.Refresh();
}
