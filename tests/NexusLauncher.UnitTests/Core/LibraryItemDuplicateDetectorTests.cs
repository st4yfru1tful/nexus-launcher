using NexusLauncher.Core.Deduplication;
using NexusLauncher.Core.Domain;

namespace NexusLauncher.UnitTests.Core;

public sealed class LibraryItemDuplicateDetectorTests
{
    private readonly LibraryItemDuplicateDetector _detector = new();

    [Fact]
    public void Deduplicate_MergesObservationsUsingTheSameSteamIdentity()
    {
        var steam = Item(
            "Portal 2",
            "steam",
            executablePath: @"D:\Steam\steamapps\common\Portal 2\portal2.exe",
            installPath: @"D:\Steam\steamapps\common\Portal 2",
            identities: new[] { new ProviderIdentity("Steam", "AppId", "620") },
            sourcePath: @"D:\Steam\steamapps\appmanifest_620.acf");
        var staleShortcut = Item(
            "Portal 2",
            "start-menu",
            executablePath: @"D:\DifferentTarget\portal2.exe",
            installPath: null,
            identities: new[] { new ProviderIdentity("Steam", "AppId", "620") },
            sourcePath: @"C:\ProgramData\Start Menu\Portal 2.lnk");

        var result = _detector.Deduplicate(new[] { steam, staleShortcut });

        var consolidated = Assert.Single(result.UniqueItems);
        Assert.Equal("steam", consolidated.ProviderId);
        Assert.Equal(@"D:\Steam\steamapps\common\Portal 2\portal2.exe", consolidated.Launch.ExecutablePath);
        Assert.Equal(2, consolidated.SourcePaths.Count);
        Assert.Contains(DuplicateMatchKind.ProviderIdentity, Assert.Single(result.DuplicateGroups).MatchKinds);
    }

    [Fact]
    public void Deduplicate_MergesRegistryAndStartMenuObservationsWithSameExecutable()
    {
        var registry = Item(
            "Blender",
            "windows-registry",
            executablePath: @"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
            installPath: @"C:\Program Files\Blender Foundation\Blender 4.5",
            identities: new[] { new ProviderIdentity("windows-registry", "uninstallKey", "Blender") },
            sourcePath: "registry-key");
        var shortcut = Item(
            "Blender",
            "start-menu",
            executablePath: @"c:/program files/blender foundation/blender 4.5/BLENDER.EXE",
            installPath: null,
            identities: new[] { new ProviderIdentity("start-menu", "shortcutPath", "blender.lnk") },
            sourcePath: "blender.lnk");

        var result = _detector.Deduplicate(new[] { registry, shortcut });

        Assert.Single(result.UniqueItems);
        Assert.Contains(DuplicateMatchKind.ExecutablePath, Assert.Single(result.DuplicateGroups).MatchKinds);
    }

    [Fact]
    public void FindDuplicateGroups_DoesNotJoinDifferentProductsInTheSameInstallDirectory()
    {
        var launcher = Item(
            "Nexus Launcher",
            "registry",
            executablePath: @"C:\Nexus\Nexus.exe",
            installPath: @"C:\Nexus",
            identities: Array.Empty<ProviderIdentity>(),
            sourcePath: "registry");
        var helperTool = Item(
            "Nexus Diagnostics",
            "registry",
            executablePath: @"C:\Nexus\Diagnostics.exe",
            installPath: @"C:\Nexus",
            identities: Array.Empty<ProviderIdentity>(),
            sourcePath: "registry");

        Assert.Empty(_detector.FindDuplicateGroups(new[] { launcher, helperTool }));
    }

    private static DiscoveredInstallation Item(
        string name,
        string provider,
        string? executablePath,
        string? installPath,
        IReadOnlyList<ProviderIdentity> identities,
        string sourcePath)
    {
        return new DiscoveredInstallation
        {
            DisplayName = name,
            ProviderId = provider,
            Category = LibraryItemCategory.Application,
            InstallPath = installPath,
            Launch = new LaunchCommand { ExecutablePath = executablePath },
            Identities = identities,
            SourcePaths = new[] { sourcePath },
        };
    }
}
