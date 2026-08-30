using System.Buffers.Binary;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NexusLauncher.UnitTests.App;

public sealed class AssetIntegrityTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void Every_static_xaml_asset_exists_and_is_packaged()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectDirectory = Path.Combine(repositoryRoot, "src", "NexusLauncher.App");
        var xaml = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.xaml"));
        var project = File.ReadAllText(Path.Combine(projectDirectory, "NexusLauncher.App.csproj"));
        var references = Regex.Matches(xaml, @"Assets/[A-Za-z0-9._-]+", RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var path = Path.Combine(projectDirectory, reference.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing XAML asset: {reference}");
            Assert.True(new FileInfo(path).Length > 32, $"XAML asset is unexpectedly empty: {reference}");
        }

        Assert.Contains("<ApplicationIcon>Assets\\NexusLauncher.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\NexusLauncher.ico\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\*.png\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Packaged_png_assets_have_valid_headers_and_dimensions()
    {
        var assetsDirectory = Path.Combine(FindRepositoryRoot(), "src", "NexusLauncher.App", "Assets");
        var pngFiles = Directory.GetFiles(assetsDirectory, "*.png");

        Assert.NotEmpty(pngFiles);
        foreach (var path in pngFiles)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length >= 24, $"PNG is truncated: {Path.GetFileName(path)}");
            Assert.True(bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature), $"Invalid PNG signature: {Path.GetFileName(path)}");
            Assert.True(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) > 0, $"PNG has no width: {Path.GetFileName(path)}");
            Assert.True(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) > 0, $"PNG has no height: {Path.GetFileName(path)}");
        }
    }

    [Fact]
    public void Windows_icon_contains_the_complete_resolution_set()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "NexusLauncher.App", "Assets", "NexusLauncher.ico");
        var bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length >= 6);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)));
        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        Assert.True(bytes.Length >= 6 + (entryCount * 16));

        var sizes = new HashSet<int>();
        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = 6 + (index * 16);
            var width = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            var height = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
            Assert.Equal(width, height);
            sizes.Add(width);

            var imageLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOffset + 8, 4));
            var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOffset + 12, 4));
            Assert.True(imageLength > 0);
            Assert.True(imageOffset + imageLength <= bytes.Length, $"ICO entry {width}x{height} points outside the file.");
        }

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 96, 128, 256], sizes.Order().ToArray());
    }

    [Fact]
    public void Repository_vector_sources_are_well_formed_svg()
    {
        var assetsDirectory = Path.Combine(FindRepositoryRoot(), "assets");
        foreach (var fileName in new[] { "nexus-icon.svg", "nexus-mark.svg" })
        {
            var document = XDocument.Load(Path.Combine(assetsDirectory, fileName));
            Assert.Equal("svg", document.Root?.Name.LocalName);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NexusLauncher.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Nexus Launcher repository root.");
    }
}
