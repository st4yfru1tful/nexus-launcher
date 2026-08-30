using NexusLauncher.Discovery.Steam;
using NexusLauncher.Discovery.Steam.Vdf;

namespace NexusLauncher.UnitTests.Steam;

public sealed class SteamVdfParserTests
{
    [Fact]
    public void Parse_ReadsNestedQuotedValuesEscapesAndComments()
    {
        const string text = """
            // Steam saves a comment before the root node
            "libraryfolders"
            {
                "0"
                {
                    "path" "D:\\SteamLibrary"
                    "label" "Main \"library\""
                    "apps"
                    {
                        "620" "123"
                    }
                }
            }
            """;

        var document = SteamVdfParser.Parse(text);
        var library = document.Root.GetObject("libraryfolders");
        Assert.NotNull(library);
        var firstFolder = library!.GetObject("0");
        Assert.NotNull(firstFolder);

        Assert.Equal(@"D:\SteamLibrary", firstFolder!.GetString("path"));
        Assert.Equal("Main \"library\"", firstFolder.GetString("label"));
        var apps = firstFolder.GetObject("apps");
        Assert.NotNull(apps);
        Assert.Equal("123", apps!.GetString("620"));
    }

    [Fact]
    public void TryParse_ReturnsFalseForAnUnclosedObject()
    {
        var success = SteamVdfParser.TryParse("\"AppState\" { \"appid\" \"620\"", out var document);

        Assert.False(success);
        Assert.Null(document);
    }

    [Fact]
    public void SteamAppManifestParser_UsesStateFlagsToDetermineInstallState()
    {
        const string installedManifest = """
            "AppState"
            {
                "appid" "620"
                "name" "Portal 2"
                "installdir" "Portal 2"
                "StateFlags" "1028"
                "buildid" "15000000"
            }
            """;
        const string incompleteManifest = """
            "AppState"
            {
                "appid" "123"
                "name" "Downloading Game"
                "installdir" "Downloading Game"
                "StateFlags" "2"
            }
            """;

        var installed = SteamAppManifestParser.Parse(installedManifest);
        var incomplete = SteamAppManifestParser.Parse(incompleteManifest);

        Assert.Equal((uint)620, installed.AppId);
        Assert.True(installed.IsInstalled);
        Assert.Equal("15000000", installed.BuildId);
        Assert.False(incomplete.IsInstalled);
    }

    [Fact]
    public void SteamLibraryFoldersParser_ReadsLegacyAndCurrentLayouts()
    {
        const string text = """
            "libraryfolders"
            {
                "0" "C:\\Steam"
                "1"
                {
                    "path" "D:\\SteamLibrary"
                    "apps"
                    {
                        "620" "123"
                        "440" "456"
                    }
                }
            }
            """;

        var folders = SteamLibraryFoldersParser.Parse(text);

        Assert.Equal(2, folders.Count);
        Assert.Equal(@"C:\Steam", folders[0].Path);
        Assert.Equal(@"D:\SteamLibrary", folders[1].Path);
        Assert.Equal(new uint[] { 440, 620 }, folders[1].AppIds);
    }
}
