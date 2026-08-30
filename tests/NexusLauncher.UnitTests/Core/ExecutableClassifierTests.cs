using NexusLauncher.Core.Classification;
using NexusLauncher.Core.Domain;

namespace NexusLauncher.UnitTests.Core;

public sealed class ExecutableClassifierTests
{
    private readonly ExecutableClassifier _classifier = new();

    [Fact]
    public void Classify_RejectsUninstallerEvenWhenItCameFromTheRegistry()
    {
        var result = _classifier.Classify(new ExecutableClassificationInput
        {
            FilePath = @"C:\Apps\Example\unins000.exe",
            IsRegisteredInstallation = true,
        });

        Assert.False(result.ShouldInclude);
        Assert.True(result.IgnoreConfidence >= 0.70d);
    }

    [Fact]
    public void Classify_GivesLauncherManifestGamesHighConfidence()
    {
        var result = _classifier.Classify(new ExecutableClassificationInput
        {
            FilePath = @"D:\SteamLibrary\steamapps\common\Portal 2\portal2.exe",
            IsFromLauncherManifest = true,
            HasGameEngineEvidence = true,
        });

        Assert.True(result.ShouldInclude);
        Assert.Equal(LibraryItemCategory.Game, result.Category);
        Assert.True(result.Confidence >= 0.95d);
    }

    [Fact]
    public void Classify_DoesNotOfferWindowsSystemBinaries()
    {
        var result = _classifier.Classify(new ExecutableClassificationInput
        {
            FilePath = @"C:\Windows\System32\notepad.exe",
            IsStartMenuTarget = true,
        });

        Assert.False(result.ShouldInclude);
    }
}
