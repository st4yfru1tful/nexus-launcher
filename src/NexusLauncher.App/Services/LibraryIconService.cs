using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLauncher.Core.Paths;

namespace NexusLauncher.App.Services;

/// <summary>
/// Extracts local Windows icons without allowing persisted library metadata to
/// initiate shell lookups against remote paths. Successful and failed lookups
/// are cached by normalized path and file modification time.
/// </summary>
public sealed class LibraryIconService
{
    private const int DefaultMaximumCacheEntries = 192;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private readonly int _maximumCacheEntries;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, long> _getLastWriteTicks;
    private readonly Func<string, ImageSource?> _extractIcon;
    private readonly object _cacheLock = new();
    private readonly Dictionary<IconCacheKey, CacheEntry> _cache = new(IconCacheKeyComparer.Instance);
    private readonly LinkedList<IconCacheKey> _leastRecentlyUsed = new();

    public LibraryIconService()
        : this(
            DefaultMaximumCacheEntries,
            File.Exists,
            path => File.GetLastWriteTimeUtc(path).Ticks,
            ExtractAssociatedIcon)
    {
    }

    internal LibraryIconService(
        int maximumCacheEntries,
        Func<string, bool> fileExists,
        Func<string, long> getLastWriteTicks,
        Func<string, ImageSource?> extractIcon)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCacheEntries);
        _maximumCacheEntries = maximumCacheEntries;
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _getLastWriteTicks = getLastWriteTicks ?? throw new ArgumentNullException(nameof(getLastWriteTicks));
        _extractIcon = extractIcon ?? throw new ArgumentNullException(nameof(extractIcon));
    }

    public static LibraryIconService Shared { get; } = new();

    /// <summary>
    /// Returns a frozen local icon, or <see langword="null"/> so the view can
    /// keep its category-vector or initial fallback visible.
    /// </summary>
    public ImageSource? GetIcon(string? iconPath, string? executablePath)
    {
        var candidates = new HashSet<string>(PathNormalizer.Comparer);
        AddCandidate(iconPath, candidates);
        AddCandidate(executablePath, candidates);

        foreach (var candidate in candidates)
        {
            var icon = GetIcon(candidate);
            if (icon is not null) return icon;
        }

        return null;
    }

    internal int CachedEntryCount
    {
        get
        {
            lock (_cacheLock) return _cache.Count;
        }
    }

    private void AddCandidate(string? value, HashSet<string> candidates)
    {
        if (!IconPathNormalizer.TryNormalize(value, out var normalized)) return;

        try
        {
            if (_fileExists(normalized)) candidates.Add(normalized);
        }
        catch (IOException)
        {
            // A disappearing or inaccessible icon source uses the view fallback.
        }
        catch (UnauthorizedAccessException)
        {
            // A disappearing or inaccessible icon source uses the view fallback.
        }
    }

    private ImageSource? GetIcon(string path)
    {
        long lastWriteTicks;
        try
        {
            lastWriteTicks = _getLastWriteTicks(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var key = new IconCacheKey(path, lastWriteTicks);
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                Touch(cached);
                return cached.Source;
            }
        }

        ImageSource? source;
        try
        {
            source = _extractIcon(path);
            if (source is Freezable { IsFrozen: false } freezable)
            {
                if (freezable.CanFreeze) freezable.Freeze();
                else source = null;
            }
        }
        catch (ArgumentException)
        {
            source = null;
        }
        catch (IOException)
        {
            source = null;
        }
        catch (UnauthorizedAccessException)
        {
            source = null;
        }

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Source;
            }

            RemoveStaleEntriesForPath(path);
            var node = _leastRecentlyUsed.AddFirst(key);
            _cache.Add(key, new CacheEntry(source, node));
            TrimCache();
        }

        return source;
    }

    private static ImageSource? ExtractAssociatedIcon(string path)
    {
        var result = SHGetFileInfo(
            path,
            fileAttributes: 0,
            out var fileInfo,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);
        if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = DestroyIcon(fileInfo.IconHandle);
        }
    }

    private void Touch(CacheEntry entry)
    {
        _leastRecentlyUsed.Remove(entry.Node);
        _leastRecentlyUsed.AddFirst(entry.Node);
    }

    private void RemoveStaleEntriesForPath(string path)
    {
        var staleKeys = _cache.Keys
            .Where(key => PathNormalizer.Comparer.Equals(key.Path, path))
            .ToArray();
        foreach (var staleKey in staleKeys) Remove(staleKey);
    }

    private void TrimCache()
    {
        while (_cache.Count > _maximumCacheEntries && _leastRecentlyUsed.Last is { } last)
        {
            Remove(last.Value);
        }
    }

    private void Remove(IconCacheKey key)
    {
        if (!_cache.Remove(key, out var entry)) return;
        _leastRecentlyUsed.Remove(entry.Node);
    }

    private readonly record struct IconCacheKey(string Path, long LastWriteTicks);
    private sealed record CacheEntry(ImageSource? Source, LinkedListNode<IconCacheKey> Node);

    private sealed class IconCacheKeyComparer : IEqualityComparer<IconCacheKey>
    {
        public static IconCacheKeyComparer Instance { get; } = new();

        public bool Equals(IconCacheKey left, IconCacheKey right) =>
            left.LastWriteTicks == right.LastWriteTicks && PathNormalizer.Comparer.Equals(left.Path, right.Path);

        public int GetHashCode(IconCacheKey key) => HashCode.Combine(
            PathNormalizer.Comparer.GetHashCode(key.Path),
            key.LastWriteTicks);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
