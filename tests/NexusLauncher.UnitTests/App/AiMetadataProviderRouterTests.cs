using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class AiMetadataProviderRouterTests
{
    [Fact]
    public async Task On_device_selection_uses_only_on_device_provider_when_it_is_unavailable()
    {
        var settings = new AppSettings { AiProvider = AiProviderMode.OnDevice };
        var onDevice = new RecordingProvider(
            "local",
            isOnDevice: true,
            new AiMetadataProviderAvailability(AiMetadataProviderState.RuntimeUnavailable, "Local setup required."));
        var cloud = new RecordingProvider("cloud", isOnDevice: false);
        var router = new AiMetadataProviderRouter(settings, onDevice, cloud);

        var availability = await router.GetAvailabilityAsync();

        Assert.False(availability.IsReady);
        Assert.Equal("local", router.ProviderId);
        Assert.True(router.IsOnDevice);
        Assert.Equal(1, onDevice.AvailabilityCalls);
        Assert.Equal(0, cloud.AvailabilityCalls);
        Assert.Equal(0, cloud.LookupCalls);
    }

    [Fact]
    public async Task Nexus_cloud_selection_uses_only_cloud_provider()
    {
        var settings = new AppSettings { AiProvider = AiProviderMode.NexusCloud };
        var onDevice = new RecordingProvider("local", isOnDevice: true);
        var cloud = new RecordingProvider("cloud", isOnDevice: false);
        var router = new AiMetadataProviderRouter(settings, onDevice, cloud);
        var request = new AiMetadataLookupRequest { Title = "Test Game" };

        var response = await router.LookupMetadataAsync(request);

        Assert.True(response.Succeeded);
        Assert.Equal("cloud", router.ProviderId);
        Assert.False(router.IsOnDevice);
        Assert.Equal(0, onDevice.AvailabilityCalls);
        Assert.Equal(0, onDevice.LookupCalls);
        Assert.Equal(0, cloud.AvailabilityCalls);
        Assert.Equal(1, cloud.LookupCalls);
    }

    [Fact]
    public async Task Router_observes_explicit_provider_changes_without_fallback()
    {
        var settings = new AppSettings { AiProvider = AiProviderMode.OnDevice };
        var onDevice = new RecordingProvider("local", isOnDevice: true);
        var cloud = new RecordingProvider("cloud", isOnDevice: false);
        var router = new AiMetadataProviderRouter(settings, onDevice, cloud);
        var request = new AiMetadataLookupRequest { Title = "Test Game" };

        await router.LookupMetadataAsync(request);
        settings.AiProvider = AiProviderMode.NexusCloud;
        await router.LookupMetadataAsync(request);

        Assert.Equal(1, onDevice.LookupCalls);
        Assert.Equal(1, cloud.LookupCalls);
        Assert.Equal("cloud", router.ProviderId);
    }

    private sealed class RecordingProvider : IAiMetadataProvider
    {
        private static readonly AiGatewayLookupResponse SuccessfulResponse = new(
            AiGatewayLookupStatus.Success,
            new AiMetadataLookupResult { CanonicalTitle = "Test Game" });

        private readonly AiMetadataProviderAvailability _availability;

        public RecordingProvider(
            string providerId,
            bool isOnDevice,
            AiMetadataProviderAvailability? availability = null)
        {
            ProviderId = providerId;
            DisplayName = providerId;
            IsOnDevice = isOnDevice;
            _availability = availability ?? new AiMetadataProviderAvailability(AiMetadataProviderState.Ready, "Ready");
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public bool IsOnDevice { get; }
        public int AvailabilityCalls { get; private set; }
        public int LookupCalls { get; private set; }

        public Task<AiMetadataProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            AvailabilityCalls++;
            return Task.FromResult(_availability);
        }

        public Task<AiGatewayLookupResponse> LookupMetadataAsync(
            AiMetadataLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            return Task.FromResult(SuccessfulResponse);
        }
    }
}
