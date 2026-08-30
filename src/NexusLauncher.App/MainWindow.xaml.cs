using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NexusLauncher.App.ViewModels;

namespace NexusLauncher.App;

public partial class MainWindow : Window, IAsyncDisposable
{
    private readonly ShellViewModel _viewModel = new();
    private IInputElement? _focusBeforeOverlay;
    private bool _overlayWasOpen;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.BeginShutdown();
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsCommandPaletteOpen) or nameof(ShellViewModel.IsOnboardingOpen) or nameof(ShellViewModel.OnboardingStep))
        {
            Dispatcher.BeginInvoke(UpdateOverlayFocus, DispatcherPriority.Input);
        }
    }

    private void UpdateOverlayFocus()
    {
        var overlayIsOpen = _viewModel.IsCommandPaletteOpen || _viewModel.IsOnboardingOpen;
        if (overlayIsOpen && !_overlayWasOpen)
        {
            _focusBeforeOverlay = Keyboard.FocusedElement;
        }

        SidebarShell.IsEnabled = !overlayIsOpen;
        ContentShell.IsEnabled = !overlayIsOpen;

        if (_viewModel.IsOnboardingOpen)
        {
            if (_viewModel.CanFinishOnboarding) OnboardingStartButton.Focus();
            else OnboardingNextButton.Focus();
        }
        else if (_viewModel.IsCommandPaletteOpen)
        {
            CommandPaletteSearchBox.Focus();
            Keyboard.Focus(CommandPaletteSearchBox);
        }
        else if (_overlayWasOpen)
        {
            if (_focusBeforeOverlay is UIElement element && element.IsVisible && element.IsEnabled)
            {
                element.Focus();
            }
            else
            {
                ContentShell.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }

            _focusBeforeOverlay = null;
        }

        _overlayWasOpen = overlayIsOpen;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsOnboardingOpen &&
            (e.Key == Key.F5 || (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))))
        {
            e.Handled = true;
            return;
        }

        if (_viewModel.IsCommandPaletteOpen && e.Key == Key.Escape)
        {
            _viewModel.IsCommandPaletteOpen = false;
            e.Handled = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _viewModel.DisposeAsync();
    }

}
