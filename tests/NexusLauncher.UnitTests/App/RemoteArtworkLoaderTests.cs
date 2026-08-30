using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class RemoteArtworkLoaderTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("https://steamstatic.com/cover.png", true)]
    [InlineData("https://shared.akamai.steamstatic.com/store_item_assets/cover.jpg", true)]
    [InlineData("HTTPS://CDN.STEAMSTATIC.COM/cover.webp", true)]
    [InlineData("http://steamstatic.com/cover.png", false)]
    [InlineData("https://steamstatic.com.example/cover.png", false)]
    [InlineData("https://evilsteamstatic.com/cover.png", false)]
    [InlineData("https://steamstatic.com.evil.example/cover.png", false)]
    [InlineData("https://user@steamstatic.com/cover.png", false)]
    [InlineData("https://steamstatic.com:444/cover.png", false)]
    [InlineData("file:///C:/cover.png", false)]
    [InlineData("not a URL", false)]
    public void Trusted_url_validation_accepts_only_https_steamstatic_hosts(string value, bool expected)
    {
        Assert.Equal(expected, RemoteArtworkLoader.TryCreateTrustedUri(value, out _));
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("IMAGE/JPEG", true)]
    [InlineData("text/html", false)]
    [InlineData("application/octet-stream", false)]
    public void Content_type_validation_requires_image_media_types(string value, bool expected)
    {
        Assert.Equal(expected, RemoteArtworkLoader.IsImageContentType(new MediaTypeHeaderValue(value)));
    }

    [Fact]
    public async Task Limited_reader_accepts_the_exact_cap_and_rejects_one_byte_more()
    {
        using var exact = new ByteArrayContent(new byte[RemoteArtworkLoader.MaximumResponseBytes]);
        using var oversized = new ByteArrayContent(new byte[RemoteArtworkLoader.MaximumResponseBytes + 1]);

        var accepted = await RemoteArtworkLoader.ReadLimitedBytesAsync(
            exact,
            RemoteArtworkLoader.MaximumResponseBytes);
        var rejected = await RemoteArtworkLoader.ReadLimitedBytesAsync(
            oversized,
            RemoteArtworkLoader.MaximumResponseBytes);

        Assert.NotNull(accepted);
        Assert.Equal(RemoteArtworkLoader.MaximumResponseBytes, accepted.Length);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task LoadAsync_decodes_an_in_memory_frozen_bitmap()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(ImageResponse(OnePixelPng)));
        using var client = new HttpClient(handler);
        var loader = new RemoteArtworkLoader(client);

        var bitmap = await loader.LoadAsync("https://shared.akamai.steamstatic.com/cover.png");
        var cached = await loader.LoadAsync("https://shared.akamai.steamstatic.com/cover.png");

        Assert.NotNull(bitmap);
        Assert.True(bitmap.IsFrozen);
        Assert.Equal(RemoteArtworkLoader.DecodePixelWidth, bitmap.PixelWidth);
        Assert.Equal(RemoteArtworkLoader.DecodePixelWidth, bitmap.PixelHeight);
        Assert.Same(bitmap, cached);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task LoadAsync_rejects_redirects_wrong_content_types_and_invalid_images()
    {
        var handler = new DelegateHandler((request, _) =>
        {
            var file = request.RequestUri!.AbsolutePath;
            if (file.EndsWith("redirect", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://example.com/untrusted.png") }
                });
            }

            if (file.EndsWith("html", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html></html>")
                });
            }

            if (file.EndsWith("followed", StringComparison.Ordinal))
            {
                var response = ImageResponse(OnePixelPng);
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/untrusted.png");
                return Task.FromResult(response);
            }

            return Task.FromResult(ImageResponse([0x01, 0x02, 0x03]));
        });
        using var client = new HttpClient(handler);
        var loader = new RemoteArtworkLoader(client);

        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/redirect"));
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/html"));
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/followed"));
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/invalid.png"));
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task LoadAsync_enforces_the_streaming_byte_cap_when_length_is_unknown()
    {
        var payload = new byte[RemoteArtworkLoader.MaximumResponseBytes + 1];
        var handler = new DelegateHandler((_, _) =>
        {
            var content = new StreamContent(new MemoryStream(payload, writable: false));
            content.Headers.ContentLength = null;
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = new HttpClient(handler);
        var loader = new RemoteArtworkLoader(client);

        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/too-large.png"));
    }

    [Fact]
    public async Task Failure_cache_is_bounded_and_uses_lru_eviction()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = new HttpClient(handler);
        var loader = new RemoteArtworkLoader(client, cacheCapacity: 2);

        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/a.png"));
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/b.png"));
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/a.png")); // refresh A
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/c.png")); // evict B
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/b.png"));

        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task Caller_cancellation_returns_null_without_poisoning_the_cache()
    {
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            if (firstRequestStarted.TrySetResult())
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return ImageResponse(OnePixelPng);
        });
        using var client = new HttpClient(handler);
        var loader = new RemoteArtworkLoader(client);
        using var cancellation = new CancellationTokenSource();

        var firstLoad = loader.LoadAsync("https://cdn.steamstatic.com/cancel.png", cancellation.Token);
        await firstRequestStarted.Task;
        cancellation.Cancel();

        Assert.Null(await firstLoad);
        Assert.NotNull(await loader.LoadAsync("https://cdn.steamstatic.com/cancel.png"));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Request_timeout_returns_null_and_is_failure_cached()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ImageResponse(OnePixelPng);
        });
        using var client = new HttpClient(handler);
        var loader = new RemoteArtworkLoader(
            client,
            cacheCapacity: 2,
            requestTimeout: TimeSpan.FromMilliseconds(25));

        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/timeout.png"));
        Assert.Null(await loader.LoadAsync("https://cdn.steamstatic.com/timeout.png"));
        Assert.Equal(1, handler.RequestCount);
    }

    private static HttpResponseMessage ImageResponse(byte[] payload)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return callback(request, cancellationToken);
        }
    }
}
