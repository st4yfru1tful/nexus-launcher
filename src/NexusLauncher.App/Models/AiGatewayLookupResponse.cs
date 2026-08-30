namespace NexusLauncher.App.Models;

public enum AiGatewayLookupStatus
{
    Success,
    NotConfigured,
    NotConnected,
    LocalRuntimeUnavailable,
    LocalModelUnavailable,
    RequestRejected,
    RateLimited,
    Unavailable,
    InvalidResponse
}

/// <summary>
/// A status-only wrapper so UI can explain a failure without receiving tokens,
/// endpoint internals, or untrusted response data.
/// </summary>
public sealed record AiGatewayLookupResponse(AiGatewayLookupStatus Status, AiMetadataLookupResult? Result = null)
{
    public bool Succeeded => Status == AiGatewayLookupStatus.Success && Result is not null;
}
