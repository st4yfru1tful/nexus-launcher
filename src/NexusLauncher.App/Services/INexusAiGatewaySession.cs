namespace NexusLauncher.App.Services;

/// <summary>
/// Represents the authenticated session for the developer-owned Nexus AI
/// gateway. It intentionally has no concept of an OpenAI token or API key.
/// </summary>
public interface INexusAiGatewaySession
{
    bool IsConfigured { get; }
    Uri? GatewayUrl { get; }
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
