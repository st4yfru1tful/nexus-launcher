using System.Text.Json;
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
            LaunchArguments = "--private"
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
        Assert.Equal("Unknown Game", item.Name);
    }

    [Fact]
    public void Ai_settings_are_disabled_by_default_and_do_not_serialize_session_material()
    {
        var settings = new AppSettings();
        var serialized = JsonSerializer.Serialize(settings);

        Assert.False(settings.EnableAiMetadata);
        Assert.Equal(25, settings.AiMonthlyRequestLimit);
        Assert.DoesNotContain("AccessToken", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
