using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NexusLauncher.App.Infrastructure;
using NexusLauncher.App.Services;
using NexusLauncher.Core.Paths;

namespace NexusLauncher.UnitTests.App;

public sealed class LibraryIconServiceTests
{
    [Theory]
    [InlineData(@"\\server\share\game.exe")]
    [InlineData(@"\\?\UNC\server\share\game.exe")]
    [InlineData("https://example.test/game.ico")]
    [InlineData("file:///C:/Apps/Game.exe")]
    [InlineData(@"relative\game.exe")]
    [InlineData(@"C:\Apps\cover.png")]
    public void Icon_path_policy_rejects_remote_relative_and_unsupported_sources(string value)
    {
        Assert.False(IconPathNormalizer.TryNormalize(value, out _));
    }

    [Fact]
    public void Icon_path_policy_accepts_quoted_local_icon_locations_with_an_index()
    {
        Assert.True(IconPathNormalizer.TryNormalize("\"C:\\Apps\\Game.exe\", -2", out var path));
        Assert.Equal(@"C:\Apps\Game.exe", path);
    }

    [Fact]
    public void Service_prefers_a_valid_icon_path_and_freezes_the_result()
    {
        string? extractedPath = null;
        var service = CreateService(
            fileExists: _ => true,
            getLastWriteTicks: _ => 1,
            extractIcon: path =>
            {
                extractedPath = path;
                return CreateImage();
            });

        var result = service.GetIcon(@"C:\Apps\Brand.ico", @"C:\Apps\Game.exe");

        Assert.NotNull(result);
        Assert.True(result.IsFrozen);
        Assert.Equal(@"C:\Apps\Brand.ico", extractedPath);
    }

    [Fact]
    public void Service_rejects_a_remote_icon_path_and_falls_back_to_the_local_executable()
    {
        string? extractedPath = null;
        var service = CreateService(
            fileExists: _ => true,
            getLastWriteTicks: _ => 1,
            extractIcon: path =>
            {
                extractedPath = path;
                return CreateImage();
            });

        var result = service.GetIcon(@"\\server\share\Brand.ico", @"C:\Apps\Game.exe");

        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\Game.exe", extractedPath);
    }

    [Fact]
    public void Service_caches_by_path_and_file_modification_time()
    {
        var extractionCount = 0;
        var lastWriteTicks = 10L;
        var service = CreateService(
            fileExists: _ => true,
            getLastWriteTicks: _ => lastWriteTicks,
            extractIcon: _ =>
            {
                extractionCount++;
                return CreateImage();
            });

        var first = service.GetIcon(null, @"C:\Apps\Game.exe");
        var second = service.GetIcon(null, @"C:\Apps\Game.exe");
        lastWriteTicks++;
        var changed = service.GetIcon(null, @"C:\Apps\Game.exe");

        Assert.Same(first, second);
        Assert.NotSame(first, changed);
        Assert.Equal(2, extractionCount);
        Assert.Equal(1, service.CachedEntryCount);
    }

    [Fact]
    public void Service_bounds_the_cache_and_negative_results_are_cached()
    {
        var extractionCount = 0;
        var service = CreateService(
            maximumCacheEntries: 2,
            fileExists: _ => true,
            getLastWriteTicks: _ => 1,
            extractIcon: _ =>
            {
                extractionCount++;
                return null;
            });

        Assert.Null(service.GetIcon(null, @"C:\Apps\One.exe"));
        Assert.Null(service.GetIcon(null, @"C:\Apps\One.exe"));
        Assert.Null(service.GetIcon(null, @"C:\Apps\Two.exe"));
        Assert.Null(service.GetIcon(null, @"C:\Apps\Three.exe"));

        Assert.Equal(3, extractionCount);
        Assert.Equal(2, service.CachedEntryCount);
    }

    [Fact]
    public void Converter_returns_null_to_leave_the_view_fallback_visible()
    {
        var service = CreateService(
            fileExists: _ => false,
            getLastWriteTicks: _ => 1,
            extractIcon: _ => throw new InvalidOperationException("Extractor must not run."));
        var converter = new LibraryIconConverter(service);

        var result = converter.Convert(
            [@"\\server\share\Brand.ico", @"relative\Game.exe"],
            typeof(ImageSource),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Null(result);
    }

    [Fact]
    public void Manual_library_items_keep_their_local_executable_as_icon_identity()
    {
        var item = ExecutableInspector.CreateFromExecutable(@"C:\Apps\Game.exe", isManual: true);

        Assert.Equal(@"C:\Apps\Game.exe", item.IconPath);
    }

    [Fact]
    public void Windows_shell_extraction_returns_a_frozen_icon_for_the_test_process()
    {
        Assert.True(OperatingSystem.IsWindows());
        var executablePath = Assert.IsType<string>(Environment.ProcessPath);

        var result = new LibraryIconService().GetIcon(null, executablePath);

        Assert.NotNull(result);
        Assert.True(result.IsFrozen);
    }

    private static LibraryIconService CreateService(
        int maximumCacheEntries = 8,
        Func<string, bool>? fileExists = null,
        Func<string, long>? getLastWriteTicks = null,
        Func<string, ImageSource?>? extractIcon = null) =>
        new(
            maximumCacheEntries,
            fileExists ?? (_ => true),
            getLastWriteTicks ?? (_ => 1),
            extractIcon ?? (_ => CreateImage()));

    private static DrawingImage CreateImage() => new(
        new GeometryDrawing(
            Brushes.White,
            pen: null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
}
