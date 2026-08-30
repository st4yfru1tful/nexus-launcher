using System.Net;
using System.Net.Http;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class SteamStoreServiceTests
{
    [Fact]
    public void ParseSearchResults_maps_a_valid_Steam_game_result()
    {
        const string payload = """
            {
              "items": [
                {
                  "type": "app",
                  "name": "Hades",
                  "id": 1145360,
                  "price": { "currency": "USD", "final": 2499 },
                  "tiny_image": "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1145360/capsule.jpg",
                  "platforms": { "windows": true, "mac": true, "linux": false }
                }
              ]
            }
            """;

        var package = Assert.Single(SteamStoreService.ParseSearchResults(payload));

        Assert.Equal("Hades", package.Name);
        Assert.Equal("1145360", package.Id);
        Assert.Equal("Steam Store", package.Source);
        Assert.Equal(StorePackageKind.Game, package.Kind);
        Assert.Equal(StorePackageAction.OpenExternalStore, package.Action);
        Assert.Equal("USD 24.99", package.Price);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1145360/capsule.jpg", package.ImageUrl);
        Assert.Equal(["Windows", "macOS"], package.Platforms);
        Assert.Equal("https://store.steampowered.com/app/1145360/", package.StoreUrl);
    }

    [Fact]
    public void ParseSearchResults_filters_invalid_entries_and_untrusted_image_urls()
    {
        const string payload = """
            {
              "items": [
                {
                  "type": "app",
                  "name": "Safe game",
                  "id": 42,
                  "tiny_image": "http://not-steam.example/cover.jpg",
                  "platforms": { "linux": true }
                },
                { "type": "bundle", "name": "Not an app", "id": 43 },
                { "type": "app", "name": "Missing id", "id": 0 },
                { "type": "app", "name": "Wrong host", "id": 44, "tiny_image": "https://steamstatic.com.example/cover.jpg" }
              ]
            }
            """;

        var results = SteamStoreService.ParseSearchResults(payload);

        Assert.Collection(results,
            package =>
            {
                Assert.Equal("Safe game", package.Name);
                Assert.Null(package.ImageUrl);
                Assert.Equal(["Linux"], package.Platforms);
                Assert.Equal("https://store.steampowered.com/app/42/", package.StoreUrl);
            },
            package =>
            {
                Assert.Equal("Wrong host", package.Name);
                Assert.Null(package.ImageUrl);
            });
    }

    [Fact]
    public void ParseSearchResults_returns_empty_for_invalid_json()
    {
        Assert.Empty(SteamStoreService.ParseSearchResults("not JSON"));
    }

    [Fact]
    public void BuildSearchUri_encodes_the_query_and_normalizes_the_country()
    {
        var uri = SteamStoreService.BuildSearchUri(" Hades & Hollow Knight ", "CA");
        var fallbackUri = SteamStoreService.BuildSearchUri("Hades", "invalid");

        Assert.Equal("https://store.steampowered.com/api/storesearch/?term=Hades%20%26%20Hollow%20Knight&l=english&cc=ca", uri.AbsoluteUri);
        Assert.Equal("?term=Hades&l=english&cc=us", fallbackUri.Query);
        Assert.Throws<ArgumentException>(() => SteamStoreService.BuildSearchUri(new string('x', 257)));
    }

    [Fact]
    public async Task SearchAsync_uses_the_store_search_endpoint_and_reports_non_success_responses()
    {
        var successHandler = new StaticResponseHandler(HttpStatusCode.OK, """
            { "items": [ { "type": "app", "name": "Hades", "id": 1145360 } ] }
            """);
        using var successClient = new HttpClient(successHandler);
        var service = new SteamStoreService(successClient);

        var results = await service.SearchAsync("Hades", "gb");

        Assert.Single(results);
        Assert.NotNull(successHandler.RequestUri);
        Assert.Equal("/api/storesearch/", successHandler.RequestUri!.AbsolutePath);
        Assert.Equal("?term=Hades&l=english&cc=gb", successHandler.RequestUri.Query);
        Assert.Equal("NexusLauncher/1.0", successHandler.UserAgent);

        var unavailableHandler = new StaticResponseHandler(HttpStatusCode.TooManyRequests, "{}");
        using var unavailableClient = new HttpClient(unavailableHandler);

        await Assert.ThrowsAsync<HttpRequestException>(() => new SteamStoreService(unavailableClient).SearchAsync("Hades"));
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string payload) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload)
            });
        }
    }
}
