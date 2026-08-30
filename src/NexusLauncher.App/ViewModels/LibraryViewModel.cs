using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class LibraryViewModel : PageViewModel
{
    private readonly ObservableCollection<LibraryItem> _library;
    private readonly LibraryService _libraryService;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private string _searchText = string.Empty;
    private string _selectedCategory = "All items";
    private bool _showHidden;
    private bool _isBusy;
    private string _status = "Ready";
    private LibraryItem? _selectedItem;

    public LibraryViewModel(
        ObservableCollection<LibraryItem> library,
        LibraryService libraryService,
        AppSettings settings,
        SettingsService settingsService)
        : base("Library", "Every game and application you choose to keep in Nexus")
    {
        _library = library;
        _libraryService = libraryService;
        _settings = settings;
        _settingsService = settingsService;
        // The library and collections pages share the same source collection, so this
        // view must not be the collection's shared default view. Each page owns its
        // filter independently.
        Items = new ListCollectionView(_library);
        Items.Filter = FilterItem;
        _library.CollectionChanged += OnLibraryChanged;
        AddExecutableCommand = new RelayCommand(AddExecutable);
        LaunchCommand = new AsyncRelayCommand(LaunchSelected, () => SelectedItem is not null);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedItem is not null);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavorite, () => SelectedItem is not null);
        HideCommand = new AsyncRelayCommand(HideSelected, () => SelectedItem is not null);
        RemoveCommand = new AsyncRelayCommand(RemoveSelected, () => SelectedItem is not null);
    }

    public ICollectionView Items { get; }
    public IReadOnlyList<string> Categories { get; } = ["All items", "Games", "Applications", "Utilities", "Development", "Media", "Launchers", "Unknown"];
    public RelayCommand AddExecutableCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand ToggleFavoriteCommand { get; }
    public AsyncRelayCommand HideCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Items.Refresh(); }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) Items.Refresh(); }
    }

    public bool ShowHidden
    {
        get => _showHidden;
        set { if (SetProperty(ref _showHidden, value)) Items.Refresh(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public LibraryItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                LaunchCommand.RaiseCanExecuteChanged();
                OpenFolderCommand.RaiseCanExecuteChanged();
                ToggleFavoriteCommand.RaiseCanExecuteChanged();
                HideCommand.RaiseCanExecuteChanged();
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task AddFromPathAsync(string executablePath)
    {
        if (_library.Any(item => string.Equals(item.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "That executable is already in your library.";
            return;
        }

        var item = LibraryService.CreateManualItem(executablePath);
        var restoredAutomaticDiscovery = LibrarySuppression.RestoreManualAddition(_settings, item);
        if (restoredAutomaticDiscovery)
        {
            await _settingsService.SaveAsync(_settings);
        }

        _library.Add(item);
        await _libraryService.SaveAsync(_library);
        SelectedItem = item;
        Status = restoredAutomaticDiscovery
            ? $"Added {item.Name}; its matching local scan exclusion was cleared."
            : $"Added {item.Name}.";
    }

    public async Task SaveAsync() => await _libraryService.SaveAsync(_library);

    private bool FilterItem(object value)
    {
        if (value is not LibraryItem item) return false;
        if (item.IsHidden && !ShowHidden) return false;
        var query = SearchText.Trim();
        if (!string.IsNullOrWhiteSpace(query) && !item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !(item.Publisher?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
            !item.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase))) return false;
        return SelectedCategory switch
        {
            "Games" => item.Category == LibraryCategory.Game,
            "Applications" => item.Category == LibraryCategory.Application,
            "Utilities" => item.Category == LibraryCategory.Utility,
            "Development" => item.Category == LibraryCategory.DevelopmentTool,
            "Media" => item.Category == LibraryCategory.MediaSoftware,
            "Launchers" => item.Category == LibraryCategory.Launcher,
            "Unknown" => item.Category == LibraryCategory.Unknown,
            _ => true
        };
    }

    private async Task LaunchSelected()
    {
        if (SelectedItem is null) return;
        try
        {
            await LibraryService.LaunchAsync(SelectedItem);
            await _libraryService.SaveAsync(_library);
            Status = $"Launched {SelectedItem.Name}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not launch", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = "Launch failed.";
        }
    }

    private void OpenSelectedFolder()
    {
        if (SelectedItem is null) return;
        try { LibraryService.OpenFolder(SelectedItem); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Could not open folder", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task ToggleFavorite()
    {
        if (SelectedItem is null) return;
        SelectedItem.IsFavorite = !SelectedItem.IsFavorite;
        Items.Refresh();
        await _libraryService.SaveAsync(_library);
    }

    private async Task HideSelected()
    {
        if (SelectedItem is null) return;
        SelectedItem.IsHidden = !SelectedItem.IsHidden;
        await _libraryService.SaveAsync(_library);
        Items.Refresh();
    }

    private async Task RemoveSelected()
    {
        if (SelectedItem is null) return;
        var item = SelectedItem;
        var answer = MessageBox.Show($"Remove {item.Name} from Nexus? This does not uninstall it or change any files.", "Remove from Nexus", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        LibrarySuppression.Suppress(_settings, item);
        await _settingsService.SaveAsync(_settings);
        _library.Remove(item);
        SelectedItem = null;
        await _libraryService.SaveAsync(_library);
        Status = $"Removed {item.Name} from Nexus. It will stay excluded from future scans; use Add executable to restore it.";
    }

    private async void AddExecutable()
    {
        var picker = new OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Add an application or game"
        };
        if (picker.ShowDialog() == true) await AddFromPathAsync(picker.FileName);
    }

    private void OnLibraryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Items));
        Items.Refresh();
    }
}
