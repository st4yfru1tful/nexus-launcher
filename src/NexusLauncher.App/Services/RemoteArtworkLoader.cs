using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Media.Imaging;

namespace NexusLauncher.App.Services;

/// <summary>
/// Downloads and decodes storefront artwork from Nexus' narrow image allowlist.
/// Network failures are deliberately represented by <see langword="null"/> so
/// callers can keep a packaged fallback visible.
/// </summary>
public sealed class RemoteArtworkLoader
{
    internal const int MaximumResponseBytes = 2 * 1024 * 1024;
    internal const int DecodePixelWidth = 360;

    private const int DefaultCacheCapacity = 64;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SuccessfulCacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailedCacheLifetime = TimeSpan.FromMinutes(1);

    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    public static RemoteArtworkLoader Shared { get; } = new(SharedHttpClient);

    private readonly HttpClient _httpClient;
    private readonly int _cacheCapacity;
    private readonly TimeSpan _requestTimeout;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _recency = new();

    public RemoteArtworkLoader(HttpClient httpClient)
        : this(httpClient, DefaultCacheCapacity, RequestTimeout)
    {
    }

    internal RemoteArtworkLoader(HttpClient httpClient, int cacheCapacity, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheCapacity);
        var effectiveTimeout = requestTimeout ?? RequestTimeout;
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        _httpClient = httpClient;
        _cacheCapacity = cacheCapacity;
        _requestTimeout = effectiveTimeout;
    }

    /// <summary>
    /// Loads a frozen bitmap for a trusted Steam CDN URL, or returns null when
    /// validation, networking, size checks, content checks, or decoding fail.
    /// </summary>
    public async Task<BitmapSource?> LoadAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!TryCreateTrustedUri(url, out var uri)) return null;

        var cacheKey = uri.AbsoluteUri;
        if (TryReadCache(cacheKey, out var cached)) return cached;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);

            var bitmap = await DownloadAsync(uri, timeout.Token).ConfigureAwait(false);
            WriteCache(cacheKey, bitmap);
            return bitmap;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation and virtualization commonly cancel obsolete loads. Do not
            // cache that result because the same artwork may still be healthy.
            return null;
        }
        catch (OperationCanceledException)
        {
            WriteCache(cacheKey, null);
            return null;
        }
        catch (Exception exception) when (IsExpectedArtworkFailure(exception))
        {
            WriteCache(cacheKey, null);
            return null;
        }
    }

    internal static bool TryCreateTrustedUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate) ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            (!candidate.IsDefaultPort && candidate.Port != 443) ||
            !IsSteamStaticHost(candidate.DnsSafeHost))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    internal static bool IsSteamStaticHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (host.Equals("steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
         host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase));

    internal static bool IsImageContentType(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType is { } mediaType &&
        mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    internal static async Task<byte[]?> ReadLimitedBytesAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        if (content.Headers.ContentLength is > 0 and var declaredLength && declaredLength > maximumBytes)
        {
            return null;
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) return null;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    internal static BitmapSource? DecodeBitmap(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = DecodePixelWidth;
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            // DecodePixelWidth bounds normal covers. Reject pathological aspect
            // ratios so a tiny compressed response cannot retain a huge surface.
            if (bitmap.PixelWidth is <= 0 or > 4096 || bitmap.PixelHeight is <= 0 or > 4096)
            {
                return null;
            }

            if (!bitmap.CanFreeze) return null;
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static HttpClient CreateSharedHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            MaxConnectionsPerServer = 6,
            UseCookies = false
        };

        return new HttpClient(handler)
        {
            // A linked token enforces the per-request timeout. Leaving HttpClient's
            // global timer disabled avoids racing two independent timeout sources.
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private async Task<BitmapSource?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        request.Headers.UserAgent.ParseAdd("NexusLauncher/1.0");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        // The production handler never follows redirects. Treat every 3xx response
        // as a failure. Also compare the final request URI so an injected client
        // with automatic redirects cannot weaken the loader's trust boundary.
        if (!response.IsSuccessStatusCode ||
            WasRedirected(uri, response.RequestMessage?.RequestUri) ||
            !IsImageContentType(response.Content.Headers.ContentType))
        {
            return null;
        }

        var payload = await ReadLimitedBytesAsync(
            response.Content,
            MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);

        return payload is null ? null : DecodeBitmap(payload);
    }

    private bool TryReadCache(string key, out BitmapSource? bitmap)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(key, out var node))
            {
                bitmap = null;
                return false;
            }

            if (node.Value.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _cache.Remove(key);
                _recency.Remove(node);
                bitmap = null;
                return false;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }
    }

    private void WriteCache(string key, BitmapSource? bitmap)
    {
        var lifetime = bitmap is null ? FailedCacheLifetime : SuccessfulCacheLifetime;
        var entry = new CacheEntry(key, bitmap, DateTimeOffset.UtcNow.Add(lifetime));

        lock (_cacheLock)
        {
            if (_cache.Remove(key, out var existing))
            {
                _recency.Remove(existing);
            }

            var node = _recency.AddFirst(entry);
            _cache.Add(key, node);

            while (_cache.Count > _cacheCapacity)
            {
                var leastRecent = _recency.Last!;
                _recency.RemoveLast();
                _cache.Remove(leastRecent.Value.Key);
            }
        }
    }

    private static bool IsExpectedArtworkFailure(Exception exception) =>
        exception is HttpRequestException or
            IOException or
            InvalidOperationException or
            ArgumentException or
            FormatException or
            NotSupportedException or
            System.Runtime.InteropServices.ExternalException;

    private static bool WasRedirected(Uri requestedUri, Uri? responseUri) =>
        responseUri is not null &&
        Uri.Compare(
            requestedUri,
            responseUri,
            UriComponents.HttpRequestUrl,
            UriFormat.UriEscaped,
            StringComparison.Ordinal) != 0;

    private sealed record CacheEntry(string Key, BitmapSource? Bitmap, DateTimeOffset ExpiresAt);
}
