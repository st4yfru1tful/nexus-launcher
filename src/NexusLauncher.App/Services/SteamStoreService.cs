using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

/// <summary>
/// Searches Steam's public storefront endpoint for discoverable catalog entries.
/// Nexus never uses this result as an ownership or installation signal; opening a
/// result remains an explicit user action in the official Steam storefront.
/// </summary>
public sealed class SteamStoreService
{
    private const int MaximumResults = 40;
    private const int MaximumResponseBytes = 512 * 1024;
    private static readonly Uri SearchEndpoint = new("https://store.steampowered.com/api/storesearch/");
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly HttpClient _httpClient;

    public SteamStoreService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedClient;
    }

    public async Task<IReadOnlyList<StorePackage>> SearchAsync(
        string query,
        string? countryCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildSearchUri(query, countryCode));
        request.Headers.UserAgent.ParseAdd("NexusLauncher/1.0");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Steam storefront search did not succeed.", null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new HttpRequestException("Steam storefront search response was too large.");
        }

        var payload = await ReadLimitedStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return ParseSearchResults(payload);
    }

    internal static Uri BuildSearchUri(string query, string? countryCode = null)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("A search query is required.", nameof(query));

        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length > 256)
        {
            throw new ArgumentException("A Steam search query must be 256 characters or fewer.", nameof(query));
        }
        var normalizedCountryCode = NormalizeCountryCode(countryCode);
        return new Uri(
            $"{SearchEndpoint}?term={Uri.EscapeDataString(normalizedQuery)}&l=english&cc={normalizedCountryCode}",
            UriKind.Absolute);
    }

    internal static IReadOnlyList<StorePackage> ParseSearchResults(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<StorePackage>();
            foreach (var item in items.EnumerateArray())
            {
                if (results.Count == MaximumResults) break;
                if (!TryParsePackage(item, out var package)) continue;
                results.Add(package);
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryParsePackage(JsonElement item, out StorePackage package)
    {
        package = null!;
        if (item.ValueKind != JsonValueKind.Object ||
            !TryGetString(item, "type", out var type) ||
            !string.Equals(type, "app", StringComparison.OrdinalIgnoreCase) ||
            !TryGetPositiveId(item, out var appId) ||
            !TryGetString(item, "name", out var name))
        {
            return false;
        }

        name = name.Trim();
        if (name.Length == 0 || name.Length > 256) return false;

        package = new StorePackage
        {
            Name = name,
            Id = appId.ToString(CultureInfo.InvariantCulture),
            Source = "Steam Store",
            Kind = StorePackageKind.Game,
            Action = StorePackageAction.OpenExternalStore,
            Price = ReadPrice(item),
            ImageUrl = ReadTrustedImageUrl(item),
            Platforms = ReadPlatforms(item),
            // Construct the storefront URL from the validated numeric app ID rather
            // than accepting a launch destination supplied by the network response.
            StoreUrl = $"https://store.steampowered.com/app/{appId.ToString(CultureInfo.InvariantCulture)}/"
        };
        return true;
    }

    private static string? ReadPrice(JsonElement item)
    {
        if (!item.TryGetProperty("price", out var price) || price.ValueKind != JsonValueKind.Object ||
            !TryGetString(price, "currency", out var currency) ||
            !IsCurrencyCode(currency) ||
            !price.TryGetProperty("final", out var final) ||
            !final.TryGetDecimal(out var minorUnits) ||
            minorUnits < 0)
        {
            return null;
        }

        return $"{currency.ToUpperInvariant()} {(minorUnits / 100m).ToString("0.00", CultureInfo.InvariantCulture)}";
    }

    private static string? ReadTrustedImageUrl(JsonElement item)
    {
        if (!TryGetString(item, "tiny_image", out var imageUrl) ||
            !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsSteamStaticHost(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static List<string> ReadPlatforms(JsonElement item)
    {
        if (!item.TryGetProperty("platforms", out var platforms) || platforms.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new List<string>(3);
        AddPlatformIfAvailable(platforms, "windows", "Windows", result);
        AddPlatformIfAvailable(platforms, "mac", "macOS", result);
        AddPlatformIfAvailable(platforms, "linux", "Linux", result);
        return result;
    }

    private static void AddPlatformIfAvailable(JsonElement platforms, string propertyName, string displayName, List<string> result)
    {
        if (platforms.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True)
        {
            result.Add(displayName);
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            (value = property.GetString() ?? string.Empty).Length > 0;
    }

    private static bool TryGetPositiveId(JsonElement item, out long appId)
    {
        appId = 0;
        return item.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.Number &&
            id.TryGetInt64(out appId) &&
            appId > 0;
    }

    private static bool IsCurrencyCode(string currency) =>
        currency.Length == 3 && currency.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static bool IsSteamStaticHost(string host) =>
        host.Equals("steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCountryCode(string? countryCode)
    {
        if (countryCode is { Length: 2 } && countryCode.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
        {
            return countryCode.ToLowerInvariant();
        }

        return "us";
    }

    private static async Task<string> ReadLimitedStringAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaximumResponseBytes) return string.Empty;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
