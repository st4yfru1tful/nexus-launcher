using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class NexusPathsTests
{
    [Fact]
    public void Portable_marker_selects_a_data_root_next_to_the_executable()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), "NexusLauncher.Tests", "portable-copy");
        var localApplicationData = Path.Combine(Path.GetTempPath(), "NexusLauncher.Tests", "local-app-data");
        var marker = Path.Combine(applicationDirectory, NexusPaths.PortableModeMarkerFileName);

        var isPortable = NexusPaths.HasPortableModeMarker(
            applicationDirectory,
            path => string.Equals(path, marker, StringComparison.OrdinalIgnoreCase));
        var root = NexusPaths.ResolveRoot(applicationDirectory, localApplicationData, isPortable);

        Assert.True(isPortable);
        Assert.Equal(Path.Combine(applicationDirectory, NexusPaths.PortableDataDirectoryName), root);
        Assert.NotEqual(Path.Combine(localApplicationData, "NexusLauncher"), root);
    }

    [Fact]
    public void Missing_portable_marker_uses_the_installed_data_root()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), "NexusLauncher.Tests", "installed-copy");
        var localApplicationData = Path.Combine(Path.GetTempPath(), "NexusLauncher.Tests", "local-app-data");

        var isPortable = NexusPaths.HasPortableModeMarker(applicationDirectory, _ => false);
        var root = NexusPaths.ResolveRoot(applicationDirectory, localApplicationData, isPortable);

        Assert.False(isPortable);
        Assert.Equal(Path.Combine(localApplicationData, "NexusLauncher"), root);
    }
}
