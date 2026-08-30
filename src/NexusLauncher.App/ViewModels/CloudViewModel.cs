using System.Windows;
using Microsoft.Win32;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.ViewModels;

public sealed class CloudViewModel : PageViewModel
{
    private readonly BackupService _backupService;
    private string _status = "Nexus keeps your library local. Export a local ZIP backup when you want to move it.";

    public CloudViewModel(BackupService backupService)
        : base("Cloud", "Portable, user-controlled library backups")
    {
        _backupService = backupService;
        ExportCommand = new RelayCommand(Export);
        RestoreCommand = new RelayCommand(Restore);
    }

    public RelayCommand ExportCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private void Export()
    {
        var picker = new SaveFileDialog
        {
            Filter = "Nexus backup (*.nexusbackup)|*.nexusbackup|ZIP archive (*.zip)|*.zip",
            FileName = $"NexusLibrary-{DateTime.Now:yyyyMMdd}.nexusbackup",
            Title = "Export Nexus library backup"
        };
        if (picker.ShowDialog() != true) return;
        try
        {
            _backupService.CreateLibraryBackup(picker.FileName);
            Status = $"Backup written to {picker.FileName}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Export backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Restore()
    {
        var picker = new OpenFileDialog
        {
            Filter = "Nexus backup (*.nexusbackup;*.zip)|*.nexusbackup;*.zip",
            Title = "Restore Nexus library backup"
        };
        if (picker.ShowDialog() != true) return;
        var confirmation = MessageBox.Show("Restoring a backup replaces the current local library and settings. Restart Nexus after this operation. Continue?", "Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            _backupService.RestoreLibraryBackup(picker.FileName);
            Status = "Backup restored. Restart Nexus to load the restored library.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Restore backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
