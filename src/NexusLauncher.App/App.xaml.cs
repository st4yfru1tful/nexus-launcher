using System.Windows;

namespace NexusLauncher.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            MessageBox.Show(
                $"Nexus encountered an unexpected error. Your library is kept locally and was not removed.\n\n{eventArgs.Exception.Message}",
                "Nexus Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            eventArgs.Handled = true;
        };
    }
}
