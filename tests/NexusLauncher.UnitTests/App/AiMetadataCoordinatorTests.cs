using System.Text.Json;
using System.Reflection;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class AiMetadataCoordinatorTests
{
    [Fact]
    public void ApplyApprovedSuggestion_updates_only_empty_description_and_new_tags()
    {
        var item = new LibraryItem
        {
            Name = "Unknown Game",
            Description = null,
            Tags = ["Local favorite"],
            ExecutablePath = @"D:\Games\Unknown\game.exe",
            InstallPath = @"D:\Games\Unknown",
            LaunchUri = "steam://run/123",
            LaunchArguments = "--private",
            WorkingDirectory = @"D:\Games\Unknown\runtime"
        };
        var suggestion = new AiMetadataLookupResult
        {
            CanonicalTitle = "A different title that stays review-only",
            Description = "A reviewable description.",
            Genres = ["Action"],
            Tags = ["action", "Roguelike"]
        };

        var updates = AiMetadataCoordinator.ApplyApprovedSuggestion(item, suggestion);

        Assert.Equal(3, updates);
        Assert.Equal("A reviewable description.", item.Description);
        Assert.Equal(["Local favorite", "Action", "Roguelike"], item.Tags);
        Assert.Equal(@"D:\Games\Unknown\game.exe", item.ExecutablePath);
        Assert.Equal(@"D:\Games\Unknown", item.InstallPath);
        Assert.Equal("steam://run/123", item.LaunchUri);
        Assert.Equal("--private", item.LaunchArguments);
        Assert.Equal(@"D:\Games\Unknown\runtime", item.WorkingDirectory);
        Assert.Equal("Unknown Game", item.Name);
    }

    [Fact]
    public void Ai_settings_are_disabled_by_default_and_do_not_serialize_session_material()
    {
        var settings = new AppSettings();
        var serialized = JsonSerializer.Serialize(settings);

        Assert.False(settings.EnableAiMetadata);
        Assert.Equal(AiProviderMode.OnDevice, settings.AiProvider);
        Assert.Equal(25, settings.AiMonthlyRequestLimit);
        Assert.DoesNotContain("AccessToken", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabled_feature_does_not_contact_the_selected_provider()
    {
        var provider = new RecordingProvider(isOnDevice: true);
        var settings = new AppSettings { EnableAiMetadata = false };
        var settingsFile = CreateTemporarySettingsService(out var temporaryDirectory);

        try
        {
            var coordinator = new AiMetadataCoordinator(settings, settingsFile, provider);

            var outcome = await coordinator.SuggestAsync(CreateLibraryItem());

            Assert.False(outcome.Succeeded);
            Assert.Equal("AI metadata suggestions are turned off in Settings.", outcome.Message);
            Assert.Equal(0, provider.AvailabilityCalls);
            Assert.Equal(0, provider.LookupCalls);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public async Task On_device_request_does_not_consume_or_save_cloud_quota()
    {
        var provider = new RecordingProvider(isOnDevice: true);
        var settings = new AppSettings
        {
            EnableAiMetadata = true,
            AiProvider = AiProviderMode.OnDevice,
            AiUsageMonth = "2000-01",
            AiRequestsThisMonth = 17,
            AiMonthlyRequestLimit = 17
        };
        var settingsService = CreateTemporarySettingsService(out var temporaryDirectory, out var settingsPath);

        try
        {
            var coordinator = new AiMetadataCoordinator(settings, settingsService, provider);
            var usageChangedCalls = 0;
            coordinator.UsageChanged += () => usageChangedCalls++;

            var outcome = await coordinator.SuggestAsync(CreateLibraryItem());

            Assert.True(outcome.Succeeded);
            Assert.True(outcome.IsOnDevice);
            Assert.Equal(1, provider.AvailabilityCalls);
            Assert.Equal(1, provider.LookupCalls);
            Assert.Equal("2000-01", settings.AiUsageMonth);
            Assert.Equal(17, settings.AiRequestsThisMonth);
            Assert.Equal(0, usageChangedCalls);
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public async Task Cloud_request_consumes_and_persists_monthly_quota()
    {
        var provider = new RecordingProvider(isOnDevice: false);
        var settings = new AppSettings
        {
            EnableAiMetadata = true,
            AiProvider = AiProviderMode.NexusCloud,
            AiUsageMonth = CurrentUsageMonth(),
            AiRequestsThisMonth = 2,
            AiMonthlyRequestLimit = 3
        };
        var settingsService = CreateTemporarySettingsService(out var temporaryDirectory, out var settingsPath);

        try
        {
            var coordinator = new AiMetadataCoordinator(settings, settingsService, provider);
            var usageChangedCalls = 0;
            coordinator.UsageChanged += () => usageChangedCalls++;

            var outcome = await coordinator.SuggestAsync(CreateLibraryItem());

            Assert.True(outcome.Succeeded);
            Assert.False(outcome.IsOnDevice);
            Assert.Equal(1, provider.LookupCalls);
            Assert.Equal(3, settings.AiRequestsThisMonth);
            Assert.Equal(1, usageChangedCalls);
            Assert.True(File.Exists(settingsPath));

            var persisted = await settingsService.LoadAsync();
            Assert.Equal(CurrentUsageMonth(), persisted.AiUsageMonth);
            Assert.Equal(3, persisted.AiRequestsThisMonth);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public async Task Cloud_request_at_quota_is_rejected_before_lookup()
    {
        var provider = new RecordingProvider(isOnDevice: false);
        var settings = new AppSettings
        {
            EnableAiMetadata = true,
            AiProvider = AiProviderMode.NexusCloud,
            AiUsageMonth = CurrentUsageMonth(),
            AiRequestsThisMonth = 4,
            AiMonthlyRequestLimit = 4
        };
        var settingsService = CreateTemporarySettingsService(out var temporaryDirectory, out var settingsPath);

        try
        {
            var coordinator = new AiMetadataCoordinator(settings, settingsService, provider);

            var outcome = await coordinator.SuggestAsync(CreateLibraryItem());

            Assert.False(outcome.Succeeded);
            Assert.Contains("request limit of 4", outcome.Message, StringComparison.Ordinal);
            Assert.Equal(1, provider.AvailabilityCalls);
            Assert.Equal(0, provider.LookupCalls);
            Assert.Equal(4, settings.AiRequestsThisMonth);
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public async Task Unavailable_on_device_provider_returns_its_actionable_message()
    {
        const string actionableMessage = "Install Ollama, download one local model, then refresh AI status.";
        var provider = new RecordingProvider(
            isOnDevice: true,
            availability: new AiMetadataProviderAvailability(
                AiMetadataProviderState.RuntimeUnavailable,
                actionableMessage));
        var settings = new AppSettings { EnableAiMetadata = true };
        var settingsService = CreateTemporarySettingsService(out var temporaryDirectory, out var settingsPath);

        try
        {
            var coordinator = new AiMetadataCoordinator(settings, settingsService, provider);

            var outcome = await coordinator.SuggestAsync(CreateLibraryItem());

            Assert.False(outcome.Succeeded);
            Assert.True(outcome.IsOnDevice);
            Assert.Equal(actionableMessage, outcome.Message);
            Assert.Equal(1, provider.AvailabilityCalls);
            Assert.Equal(0, provider.LookupCalls);
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static LibraryItem CreateLibraryItem() => new()
    {
        Name = "Test Game",
        ExecutablePath = @"C:\Games\Test Game\game.exe"
    };

    private static string CurrentUsageMonth() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private static SettingsService CreateTemporarySettingsService(out string temporaryDirectory) =>
        CreateTemporarySettingsService(out temporaryDirectory, out _);

    private static SettingsService CreateTemporarySettingsService(
        out string temporaryDirectory,
        out string settingsPath)
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "NexusLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        settingsPath = Path.Combine(temporaryDirectory, "settings.json");

        var settingsService = new SettingsService();
        var settingsFileField = typeof(SettingsService).GetField("_settingsFile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(settingsFileField);
        settingsFileField.SetValue(settingsService, settingsPath);
        return settingsService;
    }

    private sealed class RecordingProvider : IAiMetadataProvider
    {
        private static readonly AiGatewayLookupResponse SuccessfulResponse = new(
            AiGatewayLookupStatus.Success,
            new AiMetadataLookupResult
            {
                CanonicalTitle = "Test Game",
                Description = "Suggested metadata."
            });

        private readonly AiMetadataProviderAvailability _availability;
        private readonly AiGatewayLookupResponse _response;

        public RecordingProvider(
            bool isOnDevice,
            AiMetadataProviderAvailability? availability = null,
            AiGatewayLookupResponse? response = null)
        {
            IsOnDevice = isOnDevice;
            ProviderId = isOnDevice ? "test-local" : "test-cloud";
            DisplayName = isOnDevice ? "Test Local" : "Test Cloud";
            _availability = availability ?? new AiMetadataProviderAvailability(AiMetadataProviderState.Ready, "Ready");
            _response = response ?? SuccessfulResponse;
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
            return Task.FromResult(_response);
        }
    }
}
