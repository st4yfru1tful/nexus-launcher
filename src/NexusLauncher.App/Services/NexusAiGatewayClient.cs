using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

/// <summary>
/// Calls a developer-owned Nexus AI gateway over HTTPS. The desktop client
/// sends only the request produced by <see cref="AiMetadataRequestFactory"/>,
/// and never communicates directly with the OpenAI API.
/// </summary>
public sealed class NexusAiGatewayClient : IAiMetadataProvider
{
    private const int MaximumResponseBytes = 128 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly INexusAiGatewaySession _session;
    private readonly HttpClient _httpClient;

    public NexusAiGatewayClient(INexusAiGatewaySession? session = null, HttpClient? httpClient = null)
    {
        _session = session ?? new NexusAiGatewayOAuthClient();
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public bool IsConfigured => _session.IsConfigured && _session.GatewayUrl is not null;
    public string ProviderId => "nexus-cloud";
    public string DisplayName => "Nexus Cloud";
    public bool IsOnDevice => false;

    public async Task<AiMetadataProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new AiMetadataProviderAvailability(
                AiMetadataProviderState.Unavailable,
                "Nexus Cloud is not configured. On-device AI remains available as a separate provider.");
        }

        try
        {
            var token = await _session.GetAccessTokenAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(token)
                ? new AiMetadataProviderAvailability(
                    AiMetadataProviderState.Unavailable,
                    "Nexus Cloud is configured but not connected. Sign in before requesting suggestions.")
                : new AiMetadataProviderAvailability(
                    AiMetadataProviderState.Ready,
                    "Nexus Cloud is connected.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return new AiMetadataProviderAvailability(
                AiMetadataProviderState.Unavailable,
                "Nexus Cloud connection status could not be verified.");
        }
    }

    public async Task<AiGatewayLookupResponse> LookupMetadataAsync(
        AiMetadataLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsConfigured) return new AiGatewayLookupResponse(AiGatewayLookupStatus.NotConfigured);
        if (!IsSafeRequest(request)) return new AiGatewayLookupResponse(AiGatewayLookupStatus.RequestRejected);

        var accessToken = await _session.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken)) return new AiGatewayLookupResponse(AiGatewayLookupStatus.NotConnected);

        var endpoint = BuildEndpoint(_session.GatewayUrl!, "v1/metadata/lookup");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, NexusJsonOptions.Default), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new AiGatewayLookupResponse(AiGatewayLookupStatus.NotConnected);
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                return new AiGatewayLookupResponse(AiGatewayLookupStatus.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AiGatewayLookupResponse(AiGatewayLookupStatus.Unavailable);
            }

            if (!IsJson(response.Content.Headers.ContentType?.MediaType) || response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                return new AiGatewayLookupResponse(AiGatewayLookupStatus.InvalidResponse);
            }

            var payload = await ReadLimitedStringAsync(response.Content, MaximumResponseBytes, cancellationToken);
            if (payload.Length == 0) return new AiGatewayLookupResponse(AiGatewayLookupStatus.InvalidResponse);
            var result = JsonSerializer.Deserialize<AiMetadataLookupResult>(payload, ResponseJsonOptions);
            return TryNormalizeResult(result, out var normalized)
                ? new AiGatewayLookupResponse(AiGatewayLookupStatus.Success, normalized)
                : new AiGatewayLookupResponse(AiGatewayLookupStatus.InvalidResponse);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new AiGatewayLookupResponse(AiGatewayLookupStatus.Unavailable);
        }
        catch (JsonException)
        {
            return new AiGatewayLookupResponse(AiGatewayLookupStatus.InvalidResponse);
        }
    }

    internal static Uri BuildEndpoint(Uri gatewayUrl, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(gatewayUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (!string.Equals(gatewayUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            gatewayUrl.IsLoopback ||
            gatewayUrl.HostNameType != UriHostNameType.Dns ||
            gatewayUrl.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(gatewayUrl.UserInfo))
        {
            throw new InvalidOperationException("Nexus AI must use an approved HTTPS gateway.");
        }

        return new Uri(gatewayUrl.AbsoluteUri.TrimEnd('/') + "/" + relativePath.TrimStart('/'), UriKind.Absolute);
    }

    private static bool IsSafeRequest(AiMetadataLookupRequest request)
    {
        return IsSafeValue(request.Title, 256, required: true) &&
            IsSafeValue(request.Provider, 256) &&
            IsSafeValue(request.Publisher, 256) &&
            IsSafeValue(request.Version, 128) &&
            IsSafeValue(request.ExecutableFileName, 260) &&
            IsSafeValue(request.ParentFolderName, 260);
    }

    private static bool TryNormalizeResult(AiMetadataLookupResult? result, out AiMetadataLookupResult? normalized)
    {
        normalized = null;
        if (result is null ||
            !IsSafeValue(result.CanonicalTitle, 256) ||
            !IsSafeValue(result.Description, 4 * 1024) ||
            result.Confidence is < 0 or > 1)
        {
            return false;
        }

        var genres = NormalizeValues(result.Genres, 12, 64);
        var tags = NormalizeValues(result.Tags, 20, 64);
        if (genres is null || tags is null) return false;

        normalized = new AiMetadataLookupResult
        {
            CanonicalTitle = NormalizeOptional(result.CanonicalTitle),
            Description = NormalizeOptional(result.Description),
            Genres = genres,
            Tags = tags,
            Confidence = result.Confidence
        };
        return normalized.CanonicalTitle is not null || normalized.Description is not null || normalized.Genres.Count > 0 || normalized.Tags.Count > 0;
    }

    private static List<string>? NormalizeValues(IReadOnlyList<string>? values, int maximumCount, int maximumLength)
    {
        if (values is null || values.Count > maximumCount) return null;
        var normalized = new List<string>();
        foreach (var item in values)
        {
            if (!IsSafeValue(item, maximumLength, required: true)) return null;
            var value = item.Trim();
            if (!normalized.Contains(value, StringComparer.OrdinalIgnoreCase)) normalized.Add(value);
        }

        return normalized;
    }

    private static bool IsSafeValue(string? value, int maximumLength, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return !required;
        return value.Length <= maximumLength && value.All(character => !char.IsControl(character));
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsJson(string? mediaType) =>
        string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
        (mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ?? false);

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

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
}
