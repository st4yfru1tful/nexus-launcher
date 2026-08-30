using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class LibrarySuppressionTests
{
    [Fact]
    public void Suppress_records_launch_identity_and_paths_for_a_future_scan()
    {
        var settings = new AppSettings();
        var removed = new LibraryItem
        {
            Name = "Dota 2",
            LaunchUri = "steam://run/570",
            ExecutablePath = @"C:\Games\Dota 2\game.exe",
            InstallPath = @"C:\Games\Dota 2"
        };

        LibrarySuppression.Suppress(settings, removed);

        Assert.Contains("uri:steam://run/570", settings.IgnoredIdentities, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(settings.IgnoredPaths);
        Assert.True(LibrarySuppression.IsSuppressed(settings, new LibraryItem
        {
            Name = "Dota 2",
            LaunchUri = "steam://run/570"
        }));
    }

    [Fact]
    public void RestoreManualAddition_restores_matching_executable_without_reenabling_uri_duplicate()
    {
        var settings = new AppSettings();
        var removed = new LibraryItem
        {
            Name = "Dota 2",
            LaunchUri = "steam://run/570",
            ExecutablePath = @"C:\Games\Dota 2\game.exe",
            InstallPath = @"C:\Games\Dota 2"
        };
        LibrarySuppression.Suppress(settings, removed);

        var manual = new LibraryItem
        {
            Name = "Dota 2",
            ExecutablePath = @"C:\Games\Dota 2\game.exe",
            InstallPath = @"C:\Games\Dota 2"
        };

        var restored = LibrarySuppression.RestoreManualAddition(settings, manual);

        Assert.True(restored);
        Assert.False(LibrarySuppression.IsSuppressed(settings, manual));
        Assert.True(LibrarySuppression.IsSuppressed(settings, new LibraryItem
        {
            Name = "Dota 2",
            LaunchUri = "steam://run/570"
        }));
    }

    [Fact]
    public void Suppress_uses_path_boundaries_instead_of_prefix_matching()
    {
        var settings = new AppSettings();
        LibrarySuppression.Suppress(settings, new LibraryItem
        {
            Name = "Foo",
            InstallPath = @"C:\Games\Foo"
        });

        var similarlyNamedInstall = new LibraryItem
        {
            Name = "Foobar",
            ExecutablePath = @"C:\Games\Foobar\game.exe",
            InstallPath = @"C:\Games\Foobar"
        };

        Assert.False(LibrarySuppression.IsSuppressed(settings, similarlyNamedInstall));
    }
}
