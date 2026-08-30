namespace NexusLauncher.Core.Domain;

/// <summary>
/// A normalized observation made by a discovery provider before it is persisted
/// into the user's library.  Providers should report provenance instead of
/// mutating an existing library row directly.
/// </summary>
public sealed record DiscoveredInstallation
{
    public string DisplayName { get; init; } = string.Empty;

    public LibraryItemCategory Category { get; init; } = LibraryItemCategory.Unknown;

    public LaunchCommand Launch { get; init; } = new();

    public string? InstallPath { get; init; }

    /// <summary>
    /// Optional local executable, DLL, or ICO source used only for shell icon
    /// extraction. Consumers must still apply their own local-path policy.
    /// </summary>
    public string? IconPath { get; init; }

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public string ProviderId { get; init; } = string.Empty;

    public IReadOnlyList<ProviderIdentity> Identities { get; init; } = Array.Empty<ProviderIdentity>();

    /// <summary>
    /// Paths that explain where this observation came from, such as an appmanifest,
    /// registry key, or Start-menu shortcut.
    /// </summary>
    public IReadOnlyList<string> SourcePaths { get; init; } = Array.Empty<string>();

    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
}
