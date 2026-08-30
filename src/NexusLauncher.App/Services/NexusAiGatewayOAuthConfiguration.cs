namespace NexusLauncher.App.Services;

/// <summary>
/// Public OAuth client configuration for a developer-owned Nexus AI gateway.
/// It is intentionally loaded only from deployment environment variables;
/// no OpenAI credential, user account token, or client secret belongs here.
/// </summary>
public sealed record NexusAiGatewayOAuthConfiguration(
    Uri GatewayUrl,
    Uri AuthorizationUrl,
    Uri TokenUrl,
    string ClientId)
{
    public const string GatewayUrlEnvironmentVariable = "NEXUS_AI_GATEWAY_URL";
    public const string AuthorizationUrlEnvironmentVariable = "NEXUS_AI_OAUTH_AUTHORIZATION_URL";
    public const string TokenUrlEnvironmentVariable = "NEXUS_AI_OAUTH_TOKEN_URL";
    public const string ClientIdEnvironmentVariable = "NEXUS_AI_OAUTH_CLIENT_ID";

    /// <summary>
    /// Loads the configured public OAuth values. An unconfigured launcher keeps
    /// all AI gateway functionality disabled instead of falling back to a URL.
    /// </summary>
    public static bool TryLoadFromEnvironment(
        out NexusAiGatewayOAuthConfiguration? configuration,
        out string? validationError) =>
        TryLoad(Environment.GetEnvironmentVariable, out configuration, out validationError);

    internal static bool TryLoad(
        Func<string, string?> readEnvironmentVariable,
        out NexusAiGatewayOAuthConfiguration? configuration,
        out string? validationError)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        configuration = null;
        validationError = null;

        if (!TryReadHttpsUrl(readEnvironmentVariable(GatewayUrlEnvironmentVariable), GatewayUrlEnvironmentVariable, out var gatewayUrl, out validationError) ||
            !TryReadHttpsUrl(readEnvironmentVariable(AuthorizationUrlEnvironmentVariable), AuthorizationUrlEnvironmentVariable, out var authorizationUrl, out validationError) ||
            !TryReadHttpsUrl(readEnvironmentVariable(TokenUrlEnvironmentVariable), TokenUrlEnvironmentVariable, out var tokenUrl, out validationError) ||
            !TryReadClientId(readEnvironmentVariable(ClientIdEnvironmentVariable), out var clientId, out validationError))
        {
            return false;
        }

        configuration = new NexusAiGatewayOAuthConfiguration(gatewayUrl!, authorizationUrl!, tokenUrl!, clientId!);
        return true;
    }

    private static bool TryReadHttpsUrl(string? value, string variableName, out Uri? url, out string? validationError)
    {
        url = null;
        validationError = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            validationError = $"{variableName} is required to enable the Nexus AI gateway.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            parsed.IsLoopback ||
            parsed.HostNameType != UriHostNameType.Dns ||
            parsed.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            validationError = $"{variableName} must be an absolute public DNS HTTPS URL without credentials, query values, or fragments.";
            return false;
        }

        url = parsed;
        return true;
    }

    private static bool TryReadClientId(string? value, out string? clientId, out string? validationError)
    {
        clientId = null;
        validationError = null;
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 512 || normalized.Any(char.IsControl))
        {
            validationError = $"{ClientIdEnvironmentVariable} must be a non-empty public OAuth client identifier.";
            return false;
        }

        clientId = normalized;
        return true;
    }
}
