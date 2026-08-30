using System.Net;
using System.Net.Http;
using System.Text;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class NexusAiGatewayTests
{
    [Fact]
    public void OAuth_configuration_accepts_only_complete_public_https_values()
    {
        var values = new Dictionary<string, string?>
        {
            [NexusAiGatewayOAuthConfiguration.GatewayUrlEnvironmentVariable] = "https://api.nexus.example/",
            [NexusAiGatewayOAuthConfiguration.AuthorizationUrlEnvironmentVariable] = "https://identity.nexus.example/authorize",
            [NexusAiGatewayOAuthConfiguration.TokenUrlEnvironmentVariable] = "https://identity.nexus.example/token",
            [NexusAiGatewayOAuthConfiguration.ClientIdEnvironmentVariable] = "nexus-desktop-client"
        };

        var loaded = NexusAiGatewayOAuthConfiguration.TryLoad(
            key => values.TryGetValue(key, out var value) ? value : null,
            out var configuration,
            out var error);

        Assert.True(loaded);
        Assert.Null(error);
        Assert.NotNull(configuration);
        Assert.Equal("api.nexus.example", configuration.GatewayUrl.Host);
        Assert.Equal("nexus-desktop-client", configuration.ClientId);

        values[NexusAiGatewayOAuthConfiguration.GatewayUrlEnvironmentVariable] = "https://127.0.0.1:8080";
        loaded = NexusAiGatewayOAuthConfiguration.TryLoad(
            key => values.TryGetValue(key, out var value) ? value : null,
            out configuration,
            out error);

        Assert.False(loaded);
        Assert.Null(configuration);
        Assert.NotNull(error);
    }

    [Fact]
    public void Pkce_helpers_generate_valid_values_and_match_known_challenge()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var generatedVerifier = OAuthPkce.CreateCodeVerifier();
        var state = OAuthPkce.CreateState();

        Assert.True(OAuthPkce.IsValidCodeVerifier(generatedVerifier));
        Assert.True(OAuthPkce.IsValidState(state));
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OAuthPkce.CreateCodeChallenge(verifier));
        Assert.True(OAuthPkce.StateMatches(state, state));
        Assert.False(OAuthPkce.StateMatches(state, OAuthPkce.CreateState()));
    }

    [Fact]
    public void Token_response_requires_bounded_bearer_tokens_and_supports_refresh_rotation()
    {
        const string initial = """
            { "access_token": "access-one", "refresh_token": "refresh-one", "token_type": "Bearer", "expires_in": 3600, "scope": "nexus.ai.metadata" }
            """;
        const string refreshed = """
            { "access_token": "access-two", "token_type": "Bearer", "expires_in": 1800 }
            """;

        Assert.True(NexusAiGatewayOAuthClient.TryParseTokenResponse(initial, null, out var first));
        Assert.NotNull(first);
        Assert.Equal("refresh-one", first.RefreshToken);
        Assert.True(NexusAiGatewayOAuthClient.TryParseTokenResponse(refreshed, first.RefreshToken, out var second));
        Assert.NotNull(second);
        Assert.Equal("access-two", second.AccessToken);
        Assert.Equal("refresh-one", second.RefreshToken);
        Assert.False(NexusAiGatewayOAuthClient.TryParseTokenResponse("{ \"access_token\": \"only-one\" }", null, out _));
    }

    [Fact]
    public async Task Gateway_client_sends_only_the_contract_over_https_and_validates_the_response()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.OK, "application/json", """
            {
              "canonicalTitle": "Hades",
              "description": "A fast action roguelike.",
              "genres": ["Action"],
              "tags": ["Roguelike", "Mythology"],
              "confidence": 0.94
            }
            """);
        using var client = new HttpClient(handler);
        var gateway = new NexusAiGatewayClient(new StubSession("https://api.nexus.example/", "session-token"), client);
        var request = new AiMetadataLookupRequest
        {
            Title = "Hades",
            Provider = "Steam",
            ExecutableFileName = "Hades.exe",
            ParentFolderName = "x64"
        };

        var response = await gateway.LookupMetadataAsync(request);

        Assert.True(response.Succeeded);
        Assert.Equal("Hades", response.Result!.CanonicalTitle);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("session-token", handler.AuthorizationParameter);
        Assert.Equal("/v1/metadata/lookup", handler.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("ExecutablePath", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InstallPath", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LaunchArguments", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hades.exe", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gateway_client_rejects_non_json_and_does_not_call_when_not_connected()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.OK, "text/plain", "not json");
        using var client = new HttpClient(handler);
        var request = new AiMetadataLookupRequest { Title = "Hades" };

        var invalidResponse = await new NexusAiGatewayClient(new StubSession("https://api.nexus.example/", "session-token"), client).LookupMetadataAsync(request);
        var missingSession = await new NexusAiGatewayClient(new StubSession("https://api.nexus.example/", null), client).LookupMetadataAsync(request);

        Assert.Equal(AiGatewayLookupStatus.InvalidResponse, invalidResponse.Status);
        Assert.Equal(AiGatewayLookupStatus.NotConnected, missingSession.Status);
        Assert.Throws<InvalidOperationException>(() => NexusAiGatewayClient.BuildEndpoint(new Uri("http://api.nexus.example/"), "v1/metadata/lookup"));
        Assert.Throws<InvalidOperationException>(() => NexusAiGatewayClient.BuildEndpoint(new Uri("https://127.0.0.1/"), "v1/metadata/lookup"));
    }

    [Fact]
    public async Task Session_availability_requires_an_unexpired_local_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "NexusLauncher.Tests", Guid.NewGuid().ToString("N"));
        var sessionFile = Path.Combine(root, "session.dat");
        var store = new NexusAiGatewaySessionStore(sessionFile);
        var configuration = new NexusAiGatewayOAuthConfiguration(
            new Uri("https://api.nexus.example/"),
            new Uri("https://identity.nexus.example/authorize"),
            new Uri("https://identity.nexus.example/token"),
            "nexus-desktop-client");
        var client = new NexusAiGatewayOAuthClient(configuration, store);

        try
        {
            await store.SaveAsync(new NexusAiGatewaySession
            {
                AccessToken = "expired-access-token",
                RefreshToken = "refresh-token",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
            });
            Assert.False(await client.HasSessionAsync());

            await store.SaveAsync(new NexusAiGatewaySession
            {
                AccessToken = "usable-access-token",
                RefreshToken = "refresh-token",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            Assert.True(await client.HasSessionAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Session_store_encrypts_its_file_and_clears_it_on_logout()
    {
        var root = Path.Combine(Path.GetTempPath(), "NexusLauncher.Tests", Guid.NewGuid().ToString("N"));
        var sessionFile = Path.Combine(root, "session.dat");
        var store = new NexusAiGatewaySessionStore(sessionFile);
        var session = new NexusAiGatewaySession
        {
            AccessToken = "very-secret-access-token",
            RefreshToken = "very-secret-refresh-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        try
        {
            await store.SaveAsync(session);
            var stored = await File.ReadAllTextAsync(sessionFile);
            var loaded = await store.LoadAsync();

            Assert.DoesNotContain(session.AccessToken, stored, StringComparison.Ordinal);
            Assert.NotNull(loaded);
            Assert.Equal(session.AccessToken, loaded.AccessToken);

            await store.ClearAsync();
            Assert.False(File.Exists(sessionFile));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubSession(string gatewayUrl, string? accessToken) : INexusAiGatewaySession
    {
        public bool IsConfigured => true;
        public Uri? GatewayUrl { get; } = new(gatewayUrl, UriKind.Absolute);
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(accessToken);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string mediaType, string payload) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, mediaType)
            };
        }
    }
}
