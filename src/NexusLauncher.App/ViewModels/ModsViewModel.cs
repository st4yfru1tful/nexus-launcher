using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using Microsoft.Win32;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class ModsViewModel : PageViewModel
{
    private readonly ObservableCollection<LibraryItem> _library;
    private readonly ModArchiveService _archiveService;
    private LibraryItem? _selectedGame;
    private string _status = "Select a discovered game to manage a local Mods folder.";
    private bool _isBusy;

    public ModsViewModel(ObservableCollection<LibraryItem> library, ModArchiveService archiveService)
        : base("Mods", "Safe local mod archive management")
    {
        _library = library;
        _archiveService = archiveService;
        _library.CollectionChanged += OnLibraryChanged;
        ImportZipCommand = new AsyncRelayCommand(ImportZipAsync, () => SelectedGame?.InstallPath is not null && !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenModFolder, () => SelectedGame?.InstallPath is not null);
    }

    public IEnumerable<LibraryItem> Games => _library.Where(item => item.Category == LibraryCategory.Game && !item.IsHidden).OrderBy(item => item.Name);
    public AsyncRelayCommand ImportZipCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public LibraryItem? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                ImportZipCommand.RaiseCanExecuteChanged();
                OpenFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) ImportZipCommand.RaiseCanExecuteChanged(); }
    }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string? ModFolder => SelectedGame?.InstallPath is { Length: > 0 } installPath ? Path.Combine(installPath, "Mods") : null;

    private async Task ImportZipAsync()
    {
        if (SelectedGame is null || string.IsNullOrWhiteSpace(ModFolder)) return;
        var picker = new OpenFileDialog { Filter = "ZIP archives (*.zip)|*.zip", Title = "Select a mod archive" };
        if (picker.ShowDialog() != true) return;
        var confirmation = MessageBox.Show(
            $"Nexus will extract this archive to:\n{ModFolder}\n\nOnly use this for a game whose mods are intended to be placed in a Mods folder. Archive paths are checked before extraction.",
            "Install local mod archive",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var result = await _archiveService.ExtractSafelyAsync(picker.FileName, ModFolder);
            Status = $"Extracted {result.FilesExtracted} file{(result.FilesExtracted == 1 ? string.Empty : "s")} into {result.Destination}";
        }
        catch (Exception exception)
        {
            Status = "Mod archive was not installed.";
            MessageBox.Show(exception.Message, "Mod archive", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenModFolder()
    {
        if (string.IsNullOrWhiteSpace(ModFolder)) return;
        Directory.CreateDirectory(ModFolder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ModFolder) { UseShellExecute = true });
    }

    private void OnLibraryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Games));
        if (SelectedGame is not null && !_library.Contains(SelectedGame)) SelectedGame = null;
    }
}
