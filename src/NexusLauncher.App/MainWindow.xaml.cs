using System.Windows;
using NexusLauncher.App.ViewModels;

namespace NexusLauncher.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }
}
