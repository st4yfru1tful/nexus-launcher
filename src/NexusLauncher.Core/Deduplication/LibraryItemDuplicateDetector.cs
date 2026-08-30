using NexusLauncher.Core.Domain;
using NexusLauncher.Core.Paths;

namespace NexusLauncher.Core.Deduplication;

/// <summary>Why two observations were treated as the same library item.</summary>
public enum DuplicateMatchKind
{
    ProviderIdentity,
    ExecutablePath,
    InstallationPathAndName,
}

/// <summary>A group of observations representing one installed item.</summary>
public sealed record DuplicateGroup
{
    public IReadOnlyList<DiscoveredInstallation> Items { get; init; } = Array.Empty<DiscoveredInstallation>();

    public IReadOnlyList<DuplicateMatchKind> MatchKinds { get; init; } = Array.Empty<DuplicateMatchKind>();
}

/// <summary>The result of consolidating observations from multiple providers.</summary>
public sealed record DeduplicationResult
{
    public IReadOnlyList<DiscoveredInstallation> UniqueItems { get; init; } = Array.Empty<DiscoveredInstallation>();

    public IReadOnlyList<DuplicateGroup> DuplicateGroups { get; init; } = Array.Empty<DuplicateGroup>();
}

/// <summary>Finds and consolidates duplicate discovery observations.</summary>
public interface ILibraryItemDuplicateDetector
{
    IReadOnlyList<DuplicateGroup> FindDuplicateGroups(IEnumerable<DiscoveredInstallation> items);

    DeduplicationResult Deduplicate(IEnumerable<DiscoveredInstallation> items);
}

/// <summary>
/// Identity-first duplicate detector.  It only joins observations on strong keys:
/// a scoped provider identity, an exact normalized executable, or a matching
/// normalized installation path plus product name.
/// </summary>
public sealed class LibraryItemDuplicateDetector : ILibraryItemDuplicateDetector
{
    /// <inheritdoc />
    public IReadOnlyList<DuplicateGroup> FindDuplicateGroups(IEnumerable<DiscoveredInstallation> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return BuildGroups(items.ToList())
            .Select(group => new DuplicateGroup
            {
                Items = group.Indices.Select(index => group.Items[index]).ToArray(),
                MatchKinds = group.MatchKinds.OrderBy(kind => kind).ToArray(),
            })
            .ToArray();
    }

    /// <inheritdoc />
    public DeduplicationResult Deduplicate(IEnumerable<DiscoveredInstallation> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var materialized = items.ToList();
        var groups = BuildGroups(materialized);
        var groupByIndex = groups
            .SelectMany(group => group.Indices.Select(index => (Index: index, Group: group)))
            .ToDictionary(pair => pair.Index, pair => pair.Group);

        var uniqueItems = new List<DiscoveredInstallation>(materialized.Count - groups.Sum(group => group.Indices.Count - 1));
        for (var index = 0; index < materialized.Count; index++)
        {
            if (!groupByIndex.TryGetValue(index, out var group))
            {
                uniqueItems.Add(materialized[index]);
                continue;
            }

            if (group.Indices[0] == index)
            {
                uniqueItems.Add(Consolidate(group));
            }
        }

        return new DeduplicationResult
        {
            UniqueItems = uniqueItems,
            DuplicateGroups = groups.Select(group => new DuplicateGroup
            {
                Items = group.Indices.Select(index => materialized[index]).ToArray(),
                MatchKinds = group.MatchKinds.OrderBy(kind => kind).ToArray(),
            }).ToArray(),
        };
    }

    private static IndexedDuplicateGroup[] BuildGroups(List<DiscoveredInstallation> items)
    {
        if (items.Count < 2)
        {
            return Array.Empty<IndexedDuplicateGroup>();
        }

        var sets = new DisjointSet(items.Count);
        var edges = new List<DuplicateEdge>();
        JoinIndexedCandidates(items, sets, edges, GetIdentityKeys, DuplicateMatchKind.ProviderIdentity);
        JoinIndexedCandidates(items, sets, edges, GetExecutableKeys, DuplicateMatchKind.ExecutablePath);
        JoinIndexedCandidates(items, sets, edges, GetInstallAndNameKeys, DuplicateMatchKind.InstallationPathAndName);

        var groupedIndices = Enumerable.Range(0, items.Count)
            .GroupBy(sets.Find)
            .Select(group => group.OrderBy(index => index).ToArray())
            .Where(indices => indices.Length > 1)
            .OrderBy(indices => indices[0])
            .ToArray();

        return groupedIndices.Select(indices => new IndexedDuplicateGroup(
            items,
            indices,
            edges.Where(edge => sets.Find(edge.Left) == sets.Find(indices[0]))
                .Select(edge => edge.Kind)
                .Distinct()
                .ToArray())).ToArray();
    }

    private static void JoinIndexedCandidates(
        List<DiscoveredInstallation> items,
        DisjointSet sets,
        ICollection<DuplicateEdge> edges,
        Func<DiscoveredInstallation, IEnumerable<string>> keySelector,
        DuplicateMatchKind matchKind)
    {
        var index = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            foreach (var key in keySelector(items[itemIndex]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!index.TryGetValue(key, out var existingIndices))
                {
                    index[key] = new List<int> { itemIndex };
                    continue;
                }

                foreach (var existingIndex in existingIndices)
                {
                    sets.Union(existingIndex, itemIndex);
                    edges.Add(new DuplicateEdge(existingIndex, itemIndex, matchKind));
                }

                existingIndices.Add(itemIndex);
            }
        }
    }

    private static IEnumerable<string> GetIdentityKeys(DiscoveredInstallation item)
    {
        return item.Identities.Where(identity => identity.IsUsable).Select(identity => identity.CanonicalKey);
    }

    private static IEnumerable<string> GetExecutableKeys(DiscoveredInstallation item)
    {
        return PathNormalizer.TryNormalize(item.Launch.ExecutablePath, out var executable)
            ? new[] { executable }
            : Array.Empty<string>();
    }

    private static IEnumerable<string> GetInstallAndNameKeys(DiscoveredInstallation item)
    {
        if (!PathNormalizer.TryNormalize(item.InstallPath, out var installPath))
        {
            return Array.Empty<string>();
        }

        var name = CanonicalizeName(item.DisplayName);
        return name.Length == 0
            ? Array.Empty<string>()
            : new[] { string.Concat(installPath, "\u001f", name) };
    }

    private static DiscoveredInstallation Consolidate(IndexedDuplicateGroup group)
    {
        var primary = group.Indices
            .Select(index => (Item: group.Items[index], Index: index))
            .OrderByDescending(pair => GetPrimaryScore(pair.Item))
            .ThenBy(pair => pair.Index)
            .First().Item;

        var allItems = group.Indices.Select(index => group.Items[index]).ToArray();
        return primary with
        {
            InstallPath = primary.InstallPath ?? allItems.Select(item => item.InstallPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
            Publisher = primary.Publisher ?? allItems.Select(item => item.Publisher).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Version = primary.Version ?? allItems.Select(item => item.Version).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Launch = primary.Launch.IsLaunchable
                ? primary.Launch
                : allItems.Select(item => item.Launch).FirstOrDefault(command => command.IsLaunchable) ?? primary.Launch,
            Identities = allItems
                .SelectMany(item => item.Identities)
                .Where(identity => identity.IsUsable)
                .DistinctBy(identity => identity.CanonicalKey, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SourcePaths = allItems
                .SelectMany(item => item.SourcePaths)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private static int GetPrimaryScore(DiscoveredInstallation item)
    {
        var score = 0;
        score += item.Launch.IsLaunchable ? 100 : 0;
        score += item.Identities.Count(identity => identity.IsUsable) * 15;
        score += string.IsNullOrWhiteSpace(item.InstallPath) ? 0 : 8;
        score += string.IsNullOrWhiteSpace(item.Version) ? 0 : 2;
        score += item.Category == LibraryItemCategory.Unknown ? 0 : 4;
        score += item.ProviderId.Equals("steam", StringComparison.OrdinalIgnoreCase) ? 10 : 0;
        return score;
    }

    private static string CanonicalizeName(string? name)
    {
        return string.Concat((name ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToUpperInvariant));
    }

    private sealed record IndexedDuplicateGroup(
        IReadOnlyList<DiscoveredInstallation> Items,
        IReadOnlyList<int> Indices,
        IReadOnlyList<DuplicateMatchKind> MatchKinds);

    private sealed record DuplicateEdge(int Left, int Right, DuplicateMatchKind Kind);

    private sealed class DisjointSet
    {
        private readonly int[] _parents;
        private readonly byte[] _ranks;

        public DisjointSet(int count)
        {
            _parents = Enumerable.Range(0, count).ToArray();
            _ranks = new byte[count];
        }

        public int Find(int item)
        {
            while (_parents[item] != item)
            {
                _parents[item] = _parents[_parents[item]];
                item = _parents[item];
            }

            return item;
        }

        public void Union(int left, int right)
        {
            left = Find(left);
            right = Find(right);
            if (left == right)
            {
                return;
            }

            if (_ranks[left] < _ranks[right])
            {
                _parents[left] = right;
            }
            else if (_ranks[left] > _ranks[right])
            {
                _parents[right] = left;
            }
            else
            {
                _parents[right] = left;
                _ranks[left]++;
            }
        }
    }
}
