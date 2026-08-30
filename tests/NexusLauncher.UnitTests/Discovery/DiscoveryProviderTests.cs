using Microsoft.Win32;
using NexusLauncher.Core.Discovery;
using NexusLauncher.Core.Domain;
using NexusLauncher.Discovery.Abstractions;
using NexusLauncher.Discovery.Metadata;
using NexusLauncher.Discovery.Steam;
using NexusLauncher.Discovery.Windows;
using System.Runtime.Versioning;

namespace NexusLauncher.UnitTests.Discovery;

[SupportedOSPlatform("windows")]
public sealed class DiscoveryProviderTests
{
    [Fact]
    public async Task SteamProvider_DiscoversInstalledManifestAndUsesSteamUriFallback()
    {
        const string steamRoot = @"C:\Steam";
        const string installPath = @"C:\Steam\steamapps\common\Portal 2";
        var fileSystem = new FakeFileSystem(
            directories: new[] { steamRoot, @"C:\Steam\steamapps", installPath },
            files: new Dictionary<string, string>
            {
                [@"C:\Steam\steamapps\appmanifest_620.acf"] = """
                    "AppState"
                    {
                        "appid" "620"
                        "name" "Portal 2"
                        "installdir" "Portal 2"
                        "StateFlags" "4"
                    }
                    """,
            });
        var provider = new SteamDiscoveryProvider(
            installationLocator: new FixedSteamInstallationLocator(steamRoot),
            fileSystem: fileSystem,
            executableLocator: new FixedSteamExecutableLocator(@"C:\Steam\steamapps\common\Portal 2\portal2.exe"));

        var result = await provider.DiscoverAsync(new DiscoveryRequest(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Portal 2", item.DisplayName);
        Assert.Equal(LibraryItemCategory.Game, item.Category);
        Assert.Equal("steam://run/620", item.Launch.LaunchUri);
        Assert.Equal(@"C:\Steam\steamapps\common\Portal 2\portal2.exe", item.Launch.ExecutablePath);
        Assert.Equal("620", Assert.Single(item.Identities).Value);
    }

    [Fact]
    public async Task RegistryProvider_UsesDisplayIconAndExcludesInfrastructureEntries()
    {
        var registry = new FakeRegistryAccessor(new[]
        {
            new RegistryKeySnapshot("Nexus", new Dictionary<string, object?>
            {
                ["DisplayName"] = "Nexus Launcher",
                ["DisplayIcon"] = "\"C:\\Apps\\Nexus\\Nexus.exe\",0",
                ["InstallLocation"] = @"C:\Apps\Nexus",
                ["Publisher"] = "Nexus Team",
                ["DisplayVersion"] = "1.0.0",
            }),
            new RegistryKeySnapshot("Updater", new Dictionary<string, object?>
            {
                ["DisplayName"] = "Nexus Updater",
                ["DisplayIcon"] = @"C:\Apps\Nexus\updater.exe,0",
            }),
            new RegistryKeySnapshot("System", new Dictionary<string, object?>
            {
                ["DisplayName"] = "Windows Component",
                ["DisplayIcon"] = @"C:\Apps\Component\component.exe,0",
                ["SystemComponent"] = 1,
            }),
        });
        var fileSystem = new FakeFileSystem(
            directories: Array.Empty<string>(),
            files: new Dictionary<string, string>
            {
                [@"C:\Apps\Nexus\Nexus.exe"] = string.Empty,
                [@"C:\Apps\Nexus\updater.exe"] = string.Empty,
                [@"C:\Apps\Component\component.exe"] = string.Empty,
            });
        var provider = new RegistryInstalledApplicationsDiscoveryProvider(
            registry,
            fileSystem,
            new FixedMetadataReader("Nexus Launcher", "Nexus Team"));

        var result = await provider.DiscoverAsync(new DiscoveryRequest(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Nexus Launcher", item.DisplayName);
        Assert.Equal(@"C:\Apps\Nexus\Nexus.exe", item.Launch.ExecutablePath);
        Assert.Equal("Nexus Team", item.Publisher);
    }

    [Fact]
    public async Task StartMenuProvider_ResolvesShortcutToLaunchableApplication()
    {
        const string startMenuPath = @"C:\ProgramData\Start Menu\Programs";
        const string shortcutPath = @"C:\ProgramData\Start Menu\Programs\Nexus Launcher.lnk";
        const string executablePath = @"C:\Apps\Nexus\Nexus.exe";
        var fileSystem = new FakeFileSystem(
            directories: new[] { startMenuPath },
            files: new Dictionary<string, string>
            {
                [shortcutPath] = string.Empty,
                [executablePath] = string.Empty,
            });
        var provider = new StartMenuDiscoveryProvider(
            fileSystem,
            new FixedShortcutResolver(shortcutPath, executablePath),
            new FixedMetadataReader("Nexus Launcher", "Nexus Team"),
            locations: new[] { startMenuPath });

        var result = await provider.DiscoverAsync(new DiscoveryRequest(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Nexus Launcher", item.DisplayName);
        Assert.Equal(executablePath, item.Launch.ExecutablePath);
        Assert.Equal("start-menu", item.ProviderId);
    }

    [Fact]
    public async Task DiscoveryCoordinator_RecordsProviderFailureAndContinues()
    {
        var coordinator = new NexusLauncher.Discovery.DiscoveryCoordinator(new IInstallationDiscoveryProvider[]
        {
            new ThrowingProvider(),
            new FixedProvider(),
        });

        var result = await coordinator.DiscoverAsync(new DiscoveryRequest(), CancellationToken.None);

        Assert.Single(result.Items);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("failing", issue.ProviderId);
    }

    private sealed class FixedSteamInstallationLocator : ISteamInstallationLocator
    {
        private readonly SteamInstallation _installation;

        public FixedSteamInstallationLocator(string rootPath)
        {
            _installation = new SteamInstallation(rootPath);
        }

        public IReadOnlyList<SteamInstallation> FindInstallations() => new[] { _installation };
    }

    private sealed class FixedSteamExecutableLocator : ISteamExecutableLocator
    {
        private readonly string _path;

        public FixedSteamExecutableLocator(string path)
        {
            _path = path;
        }

        public string? FindBestExecutable(string installPath, string displayName, CancellationToken cancellationToken) => _path;
    }

    private sealed class FakeRegistryAccessor : IRegistryAccessor
    {
        private readonly IReadOnlyList<RegistryKeySnapshot> _entries;

        public FakeRegistryAccessor(IReadOnlyList<RegistryKeySnapshot> entries)
        {
            _entries = entries;
        }

        public object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName) => null;

        public IReadOnlyList<RegistryKeySnapshot> EnumerateSubKeys(RegistryHive hive, RegistryView view, string subKeyPath)
        {
            return hive == RegistryHive.CurrentUser ? _entries : Array.Empty<RegistryKeySnapshot>();
        }
    }

    private sealed class FixedMetadataReader : IExecutableMetadataReader
    {
        private readonly string _productName;
        private readonly string _companyName;

        public FixedMetadataReader(string productName, string companyName)
        {
            _productName = productName;
            _companyName = companyName;
        }

        public ExecutableMetadata Read(string executablePath) => new()
        {
            FilePath = executablePath,
            ProductName = _productName,
            CompanyName = _companyName,
            FileVersion = "1.0.0",
        };
    }

    private sealed class FixedShortcutResolver : IShortcutResolver
    {
        private readonly string _shortcutPath;
        private readonly string _targetPath;

        public FixedShortcutResolver(string shortcutPath, string targetPath)
        {
            _shortcutPath = shortcutPath;
            _targetPath = targetPath;
        }

        public bool TryResolve(string shortcutPath, out ShortcutTarget target)
        {
            target = new ShortcutTarget
            {
                TargetPath = _targetPath,
                WorkingDirectory = Path.GetDirectoryName(_targetPath),
            };
            return string.Equals(shortcutPath, _shortcutPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeFileSystem : IDiscoveryFileSystem
    {
        private readonly HashSet<string> _directories;
        private readonly Dictionary<string, string> _files;

        public FakeFileSystem(IEnumerable<string> directories, IReadOnlyDictionary<string, string> files)
        {
            _directories = directories.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _files = files.ToDictionary(pair => Normalize(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

        public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

        public string ReadAllText(string path) => _files[Normalize(path)];

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            var normalizedDirectory = Normalize(path);
            var recursive = searchOption == SearchOption.AllDirectories;
            return _files.Keys
                .Where(file => recursive
                    ? file.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(Path.GetDirectoryName(file), normalizedDirectory, StringComparison.OrdinalIgnoreCase))
                .Where(file => MatchesSearchPattern(Path.GetFileName(file), searchPattern))
                .ToArray();
        }

        private static bool MatchesSearchPattern(string fileName, string searchPattern)
        {
            return searchPattern.Equals("*.lnk", StringComparison.OrdinalIgnoreCase)
                ? fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                : searchPattern.Equals("appmanifest_*.acf", StringComparison.OrdinalIgnoreCase)
                    ? fileName.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) &&
                      fileName.EndsWith(".acf", StringComparison.OrdinalIgnoreCase)
                    : searchPattern.Equals("*.exe", StringComparison.OrdinalIgnoreCase)
                        ? fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        : string.Equals(fileName, searchPattern, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }

    private sealed class ThrowingProvider : IInstallationDiscoveryProvider
    {
        public string Id => "failing";

        public Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("expected test provider failure");
        }
    }

    private sealed class FixedProvider : IInstallationDiscoveryProvider
    {
        public string Id => "working";

        public Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DiscoveryResult
            {
                ProviderId = Id,
                Items = new[] { new DiscoveredInstallation { DisplayName = "Working item", ProviderId = Id } },
            });
        }
    }
}
