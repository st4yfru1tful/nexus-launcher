using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed record NexusAiOAuthConnectionResult(bool Succeeded, string Message)
{
    public static NexusAiOAuthConnectionResult Success() => new(true, "Nexus AI is connected for this Windows user.");
    public static NexusAiOAuthConnectionResult Failure(string message) => new(false, message);
}

/// <summary>
/// OAuth 2.0 authorization-code-with-PKCE client for a developer-owned Nexus
/// AI gateway. This deliberately does not authenticate against OpenAI: OpenAI
/// does not expose a user OAuth flow for direct desktop API calls.
/// </summary>
public sealed class NexusAiGatewayOAuthClient : INexusAiGatewaySession
{
    private const int MaximumTokenResponseLength = 64 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly NexusAiGatewayOAuthConfiguration? _configuration;
    private readonly NexusAiGatewaySessionStore _sessionStore;
    private readonly HttpClient _httpClient;

    public NexusAiGatewayOAuthClient(
        NexusAiGatewayOAuthConfiguration? configuration = null,
        NexusAiGatewaySessionStore? sessionStore = null,
        HttpClient? httpClient = null)
    {
        if (configuration is null)
        {
            NexusAiGatewayOAuthConfiguration.TryLoadFromEnvironment(out configuration, out _);
        }

        _configuration = configuration;
        _sessionStore = sessionStore ?? new NexusAiGatewaySessionStore();
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public bool IsConfigured => _configuration is not null;
    public Uri? GatewayUrl => _configuration?.GatewayUrl;
    public string AvailabilityMessage => IsConfigured
        ? "Nexus AI is ready to connect."
        : "Nexus AI is not configured in this build. Local library features remain available.";

    public async Task<bool> HasSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = await _sessionStore.LoadAsync(cancellationToken);
        return session?.IsAccessTokenUsable(DateTimeOffset.UtcNow) == true;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_configuration is null) return null;

        var session = await _sessionStore.LoadAsync(cancellationToken);
        if (session is null) return null;
        if (session.IsAccessTokenUsable(DateTimeOffset.UtcNow)) return session.AccessToken;

        var refreshed = await RefreshSessionAsync(session, cancellationToken);
        if (refreshed is null) return null;

        await _sessionStore.SaveAsync(refreshed, cancellationToken);
        return refreshed.AccessToken;
    }

    public async Task<NexusAiOAuthConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_configuration is null)
        {
            return NexusAiOAuthConnectionResult.Failure("Nexus AI is not configured in this build, so there is no service to sign in to.");
        }

        HttpListener? listener = null;
        try
        {
            var verifier = OAuthPkce.CreateCodeVerifier();
            var state = OAuthPkce.CreateState();
            listener = CreateLoopbackListener(out var redirectUri);
            listener.Start();
            var authorizationUri = BuildAuthorizationUri(_configuration, redirectUri, state, verifier);
            if (Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri) { UseShellExecute = true }) is null)
            {
                return NexusAiOAuthConnectionResult.Failure("Nexus AI could not open a browser for sign-in on this device.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            if (context.Request.RemoteEndPoint is not { Address: var address } || !IPAddress.IsLoopback(address))
            {
                await WriteCallbackAsync(context.Response, false);
                return NexusAiOAuthConnectionResult.Failure("The Nexus AI sign-in callback was not received from this computer.");
            }

            var returnedState = context.Request.QueryString["state"];
            var authorizationCode = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];
            if (!string.IsNullOrWhiteSpace(error) ||
                !OAuthPkce.StateMatches(state, returnedState) ||
                string.IsNullOrWhiteSpace(authorizationCode) ||
                authorizationCode.Length > 16 * 1024 ||
                authorizationCode.Any(char.IsControl))
            {
                await WriteCallbackAsync(context.Response, false);
                return NexusAiOAuthConnectionResult.Failure("Nexus AI sign-in was cancelled or could not be verified.");
            }

            var session = await ExchangeAuthorizationCodeAsync(authorizationCode, redirectUri, verifier, timeout.Token);
            if (session is null)
            {
                await WriteCallbackAsync(context.Response, false);
                return NexusAiOAuthConnectionResult.Failure("Nexus AI did not return a valid session. No local library data was sent.");
            }

            await _sessionStore.SaveAsync(session, timeout.Token);
            await WriteCallbackAsync(context.Response, true);
            return NexusAiOAuthConnectionResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NexusAiOAuthConnectionResult.Failure("Nexus AI sign-in was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return NexusAiOAuthConnectionResult.Failure("Nexus AI sign-in timed out. Try again when you are ready.");
        }
        catch (HttpRequestException)
        {
            return NexusAiOAuthConnectionResult.Failure("Nexus AI could not be reached. Check your connection and try again.");
        }
        catch (InvalidOperationException)
        {
            return NexusAiOAuthConnectionResult.Failure("Nexus AI sign-in could not start on this device.");
        }
        catch (Exception)
        {
            return NexusAiOAuthConnectionResult.Failure("Nexus AI sign-in could not be completed on this device. No library metadata was sent.");
        }
        finally
        {
            listener?.Close();
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => _sessionStore.ClearAsync(cancellationToken);

    internal static Uri BuildAuthorizationUri(
        NexusAiGatewayOAuthConfiguration configuration,
        Uri redirectUri,
        string state,
        string verifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(redirectUri);
        if (!OAuthPkce.IsValidState(state)) throw new ArgumentException("OAuth state is invalid.", nameof(state));

        var challenge = OAuthPkce.CreateCodeChallenge(verifier);
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["scope"] = "nexus.ai.metadata"
        };
        var query = string.Join("&", parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var builder = new UriBuilder(configuration.AuthorizationUrl)
        {
            Query = string.IsNullOrWhiteSpace(configuration.AuthorizationUrl.Query)
                ? query
                : configuration.AuthorizationUrl.Query.TrimStart('?') + "&" + query
        };
        return builder.Uri;
    }

    internal static bool TryParseTokenResponse(
        string payload,
        string? fallbackRefreshToken,
        out NexusAiGatewaySession? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumTokenResponseLength) return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetBoundedString(root, "access_token", 16 * 1024, out var accessToken))
            {
                return false;
            }

            var refreshToken = TryGetBoundedString(root, "refresh_token", 16 * 1024, out var returnedRefresh)
                ? returnedRefresh
                : fallbackRefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken)) return false;

            var tokenType = TryGetBoundedString(root, "token_type", 32, out var returnedTokenType)
                ? returnedTokenType
                : "Bearer";
            var scope = TryGetBoundedString(root, "scope", 1024, out var returnedScope) ? returnedScope : null;
            var lifetimeSeconds = root.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var parsedLifetime)
                ? parsedLifetime
                : 3600;
            if (lifetimeSeconds is < 60 or > 86_400) return false;

            session = new NexusAiGatewaySession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds),
                TokenType = tokenType,
                Scope = scope
            };
            session.EnsureValid();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static HttpListener CreateLoopbackListener(out Uri redirectUri)
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        redirectUri = new Uri($"http://127.0.0.1:{port}/nexus-ai-oauth/", UriKind.Absolute);
        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.AbsoluteUri);
        return listener;
    }

    private async Task<NexusAiGatewaySession?> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        Uri redirectUri,
        string verifier,
        CancellationToken cancellationToken)
    {
        if (_configuration is null || authorizationCode.Length > 16 * 1024) return null;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["client_id"] = _configuration.ClientId,
            ["code_verifier"] = verifier
        };
        return await RequestSessionAsync(form, null, cancellationToken);
    }

    private async Task<NexusAiGatewaySession?> RefreshSessionAsync(NexusAiGatewaySession session, CancellationToken cancellationToken)
    {
        if (_configuration is null) return null;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.RefreshToken,
            ["client_id"] = _configuration.ClientId
        };
        return await RequestSessionAsync(form, session.RefreshToken, cancellationToken);
    }

    private async Task<NexusAiGatewaySession?> RequestSessionAsync(
        IReadOnlyDictionary<string, string> form,
        string? fallbackRefreshToken,
        CancellationToken cancellationToken)
    {
        if (_configuration is null) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, _configuration.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaximumTokenResponseLength) return null;

        var payload = await ReadLimitedStringAsync(response.Content, MaximumTokenResponseLength, cancellationToken);
        return TryParseTokenResponse(payload, fallbackRefreshToken, out var session) ? session : null;
    }

    private static async Task WriteCallbackAsync(HttpListenerResponse response, bool success)
    {
        response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        response.ContentType = "text/html; charset=utf-8";
        var body = success
            ? "<html><body><h2>Nexus AI connected</h2><p>You can close this window and return to Nexus.</p></body></html>"
            : "<html><body><h2>Nexus AI was not connected</h2><p>You can close this window and return to Nexus.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task<string> ReadLimitedStringAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maximumBytes) return string.Empty;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static bool TryGetBoundedString(JsonElement element, string propertyName, int maximumLength, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0 && value.Length <= maximumLength && value.All(character => !char.IsControl(character));
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
}
