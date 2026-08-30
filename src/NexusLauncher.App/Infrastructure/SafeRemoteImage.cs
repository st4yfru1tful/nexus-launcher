using System.Windows;
using System.Windows.Controls;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.Infrastructure;

/// <summary>
/// An image control that safely loads allowlisted remote storefront artwork.
/// Keep a packaged image behind this control to provide a deterministic fallback.
/// </summary>
public sealed class SafeRemoteImage : Image
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
        nameof(Url),
        typeof(string),
        typeof(SafeRemoteImage),
        new FrameworkPropertyMetadata(null, OnUrlChanged));

    private CancellationTokenSource? _loadCancellation;
    private long _loadVersion;

    public SafeRemoteImage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    internal RemoteArtworkLoader ArtworkLoader { get; set; } = RemoteArtworkLoader.Shared;

    private static void OnUrlChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var image = (SafeRemoteImage)dependencyObject;
        image.CancelCurrentLoad(clearSource: true);
        if (image.IsLoaded)
        {
            image.BeginCurrentLoad();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        CancelCurrentLoad(clearSource: true);
        BeginCurrentLoad();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs) => CancelCurrentLoad(clearSource: true);

    private void BeginCurrentLoad()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var version = ++_loadVersion;
        LoadCurrentAsync(Url, version, cancellation);
    }

    private async void LoadCurrentAsync(string url, long version, CancellationTokenSource cancellation)
    {
        try
        {
            var bitmap = await ArtworkLoader.LoadAsync(url, cancellation.Token);
            if (!cancellation.IsCancellationRequested &&
                ReferenceEquals(_loadCancellation, cancellation) &&
                version == _loadVersion)
            {
                Source = bitmap;
            }
        }
        catch (Exception)
        {
            // The loader is already failure-safe. This final UI boundary ensures a
            // custom/injected loader can never surface an async-void exception.
            if (!cancellation.IsCancellationRequested && version == _loadVersion)
            {
                Source = null;
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelCurrentLoad(bool clearSource)
    {
        _loadVersion++;
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        cancellation?.Cancel();

        if (clearSource)
        {
            Source = null;
        }
    }
}
