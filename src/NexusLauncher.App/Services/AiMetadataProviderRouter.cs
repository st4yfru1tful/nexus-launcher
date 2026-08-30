using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

/// <summary>
/// Routes each request to the provider explicitly selected in local settings.
/// It intentionally has no fallback path between local and cloud providers.
/// </summary>
public sealed class AiMetadataProviderRouter : IAiMetadataProvider
{
    private readonly AppSettings _settings;
    private readonly IAiMetadataProvider _onDevice;
    private readonly IAiMetadataProvider _nexusCloud;

    public AiMetadataProviderRouter(
        AppSettings settings,
        IAiMetadataProvider onDevice,
        IAiMetadataProvider nexusCloud)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _onDevice = onDevice ?? throw new ArgumentNullException(nameof(onDevice));
        _nexusCloud = nexusCloud ?? throw new ArgumentNullException(nameof(nexusCloud));
    }

    public IAiMetadataProvider CurrentProvider =>
        _settings.AiProvider == AiProviderMode.NexusCloud ? _nexusCloud : _onDevice;

    public string ProviderId => CurrentProvider.ProviderId;
    public string DisplayName => CurrentProvider.DisplayName;
    public bool IsOnDevice => CurrentProvider.IsOnDevice;

    public Task<AiMetadataProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        CurrentProvider.GetAvailabilityAsync(cancellationToken);

    public Task<AiGatewayLookupResponse> LookupMetadataAsync(
        AiMetadataLookupRequest request,
        CancellationToken cancellationToken = default) =>
        CurrentProvider.LookupMetadataAsync(request, cancellationToken);
}
