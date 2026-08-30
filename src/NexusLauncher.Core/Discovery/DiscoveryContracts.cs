using NexusLauncher.Core.Domain;

namespace NexusLauncher.Core.Discovery;

/// <summary>Represents a bounded request to an installation discovery provider.</summary>
public sealed record DiscoveryRequest
{
    /// <summary>
    /// A quick request should limit itself to provider-owned manifests, registry
    /// records, and shortcuts rather than performing broad drive scans.
    /// </summary>
    public bool IsQuickScan { get; init; } = true;

    /// <summary>User-selected paths that a provider may use when applicable.</summary>
    public IReadOnlyList<string> AdditionalSearchPaths { get; init; } = Array.Empty<string>();
}

/// <summary>One non-fatal issue encountered during an otherwise isolated scan.</summary>
public sealed record DiscoveryIssue(string ProviderId, string Message, bool IsTransient = false);

/// <summary>The complete output from a single discovery provider invocation.</summary>
public sealed record DiscoveryResult
{
    public string ProviderId { get; init; } = string.Empty;

    public IReadOnlyList<DiscoveredInstallation> Items { get; init; } = Array.Empty<DiscoveredInstallation>();

    public IReadOnlyList<DiscoveryIssue> Issues { get; init; } = Array.Empty<DiscoveryIssue>();
}

/// <summary>
/// A provider that finds installed software without modifying the user's library.
/// Providers are intentionally independent so a broken manifest cannot abort a
/// complete library scan.
/// </summary>
public interface IInstallationDiscoveryProvider
{
    string Id { get; }

    Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken);
}
