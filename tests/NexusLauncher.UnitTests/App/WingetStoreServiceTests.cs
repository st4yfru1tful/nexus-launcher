using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class WingetStoreServiceTests
{
    [Fact]
    public void ParseSearchResults_parses_current_WinGet_match_column_output()
    {
        const string output = """
            Name                     Id                              Version      Match                      Source
            -------------------------------------------------------------------------------------------------------
            Microsoft PowerToys      Microsoft.PowerToys              0.101.0      Tag: powertoys            winget
            PowerToys Preview        Microsoft.PowerToys.Preview      0.102.0      Tag: powertoys            winget
            """;

        var results = WingetStoreService.ParseSearchResults(output);

        Assert.Collection(results,
            package =>
            {
                Assert.Equal("Microsoft PowerToys", package.Name);
                Assert.Equal("Microsoft.PowerToys", package.Id);
                Assert.Equal("0.101.0", package.Version);
                Assert.Equal("winget", package.Source);
            },
            package => Assert.Equal("Microsoft.PowerToys.Preview", package.Id));
    }

    [Fact]
    public void ParseSearchResults_parses_legacy_output_without_match_column()
    {
        const string output = """
            Name      Id                  Version      Source
            --------------------------------------------------
            PowerToys Microsoft.PowerToys 0.101.2362.0 winget
            """;

        var package = Assert.Single(WingetStoreService.ParseSearchResults(output));

        Assert.Equal("PowerToys", package.Name);
        Assert.Equal("Microsoft.PowerToys", package.Id);
        Assert.Equal("0.101.2362.0", package.Version);
        Assert.Equal("winget", package.Source);
    }
}
