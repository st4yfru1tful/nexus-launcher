using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public enum AiMetadataProviderState
{
    Ready,
    RuntimeUnavailable,
    NoLocalModel,
    Unavailable
}

public sealed record AiMetadataProviderAvailability(
    AiMetadataProviderState State,
    string Message,
    string? ModelName = null)
{
    public bool IsReady => State == AiMetadataProviderState.Ready;
}

/// <summary>
/// A bounded source of reviewable metadata suggestions. Implementations must
/// preserve the privacy contract represented by <see cref="AiMetadataLookupRequest"/>.
/// </summary>
public interface IAiMetadataProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool IsOnDevice { get; }

    Task<AiMetadataProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<AiGatewayLookupResponse> LookupMetadataAsync(
        AiMetadataLookupRequest request,
        CancellationToken cancellationToken = default);
}
