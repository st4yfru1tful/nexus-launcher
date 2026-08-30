using NexusLauncher.Core.Paths;

namespace NexusLauncher.UnitTests.Core;

public sealed class PathNormalizerTests
{
    [Fact]
    public void Normalize_RemovesQuotesDotSegmentsAndTrailingSeparator()
    {
        var normalized = PathNormalizer.Normalize("  \"C:\\Games\\Nexus\\bin\\..\\Game.exe\"  ");

        Assert.Equal(@"C:\Games\Nexus\Game.exe", normalized);
    }

    [Fact]
    public void AreEquivalent_UsesWindowsCasingAndSeparatorRules()
    {
        Assert.True(PathNormalizer.AreEquivalent(
            @"C:\\Games\\Nexus\\",
            @"c:/games/nexus"));
    }

    [Fact]
    public void IsDescendantOrSelf_RequiresADirectoryBoundary()
    {
        Assert.True(PathNormalizer.IsDescendantOrSelf(@"C:\Apps\Nexus\Game.exe", @"C:\Apps\Nexus"));
        Assert.False(PathNormalizer.IsDescendantOrSelf(@"C:\Apps\NexusTwo\Game.exe", @"C:\Apps\Nexus"));
    }
}
