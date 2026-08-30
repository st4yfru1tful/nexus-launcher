using System.Text.Json;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class AiMetadataRequestFactoryTests
{
    [Fact]
    public void Create_includes_only_minimal_safe_metadata()
    {
        var item = new LibraryItem
        {
            Name = "Celestial Drift",
            Provider = "Steam",
            Publisher = "Northstar Studios",
            Version = "2.4.1",
            ExecutablePath = @"C:\Users\Example\Games\Celestial Drift\Binaries\Win64\CelestialDrift.exe",
            InstallPath = @"C:\Users\Example\Games\Celestial Drift",
            WorkingDirectory = @"C:\Users\Example\Games\Celestial Drift\Binaries\Win64",
            LaunchUri = "steam://run/987654",
            LaunchArguments = "--auth-token=must-not-leave-device",
            IconPath = @"C:\Users\Example\Games\Celestial Drift\cover.png",
            Description = "Local notes that must not leave the device.",
            Tags = ["private-local-tag"]
        };

        var request = AiMetadataRequestFactory.Create(item);
        var outboundJson = JsonSerializer.Serialize(request);

        Assert.Equal("Celestial Drift", request.Title);
        Assert.Equal("Steam", request.Provider);
        Assert.Equal("Northstar Studios", request.Publisher);
        Assert.Equal("2.4.1", request.Version);
        Assert.Equal("CelestialDrift.exe", request.ExecutableFileName);
        Assert.Equal("Win64", request.ParentFolderName);
        Assert.Equal(
            ["ExecutableFileName", "ParentFolderName", "Provider", "Publisher", "Title", "Version"],
            typeof(AiMetadataLookupRequest).GetProperties().Select(property => property.Name).OrderBy(name => name));

        Assert.DoesNotContain("C:\\Users\\Example", outboundJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("steam://run/987654", outboundJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must-not-leave-device", outboundJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cover.png", outboundJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Local notes", outboundJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-local-tag", outboundJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_does_not_fall_back_to_install_path_or_launch_uri()
    {
        var item = new LibraryItem
        {
            Name = "URI-only entry",
            InstallPath = @"D:\Secret Library\Never Send This",
            LaunchUri = "steam://run/1234"
        };

        var request = AiMetadataRequestFactory.Create(item);
        var outboundJson = JsonSerializer.Serialize(request);

        Assert.Null(request.ExecutableFileName);
        Assert.Null(request.ParentFolderName);
        Assert.DoesNotContain("Secret Library", outboundJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Never Send This", outboundJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("steam://", outboundJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_a_non_file_uri_misfiled_as_an_executable_path()
    {
        var item = new LibraryItem
        {
            Name = "URI masquerading as a path",
            ExecutablePath = "steam://run/570"
        };

        var request = AiMetadataRequestFactory.Create(item);

        Assert.Null(request.ExecutableFileName);
        Assert.Null(request.ParentFolderName);
    }
}
