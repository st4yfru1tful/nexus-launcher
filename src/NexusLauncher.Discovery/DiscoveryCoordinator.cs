using NexusLauncher.Core.Discovery;
using NexusLauncher.Core.Domain;

namespace NexusLauncher.Discovery;

/// <summary>The aggregate output of an isolated multi-provider scan.</summary>
public sealed record DiscoveryScanResult
{
    public IReadOnlyList<DiscoveredInstallation> Items { get; init; } = Array.Empty<DiscoveredInstallation>();

    public IReadOnlyList<DiscoveryIssue> Issues { get; init; } = Array.Empty<DiscoveryIssue>();
}

/// <summary>
/// Runs discovery providers independently.  A provider failure becomes a visible
/// diagnostic issue rather than breaking Steam, registry, or Start-menu discovery.
/// </summary>
public sealed class DiscoveryCoordinator
{
    private readonly IReadOnlyList<IInstallationDiscoveryProvider> _providers;

    public DiscoveryCoordinator(IEnumerable<IInstallationDiscoveryProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
    }

    public async Task<DiscoveryScanResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<DiscoveredInstallation>();
        var issues = new List<DiscoveryIssue>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await provider.DiscoverAsync(request, cancellationToken).ConfigureAwait(false);
                items.AddRange(result.Items);
                issues.AddRange(result.Issues);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                issues.Add(new DiscoveryIssue(provider.Id, $"Discovery provider failed: {exception.Message}", IsTransient: true));
            }
        }

        return new DiscoveryScanResult
        {
            Items = items,
            Issues = issues,
        };
    }
}
