namespace NexusLauncher.App.Models;

public enum StorePackageKind
{
    Software,
    Game
}

public enum StorePackageAction
{
    InstallWithWinget,
    OpenExternalStore
}

public sealed class StorePackage
{
    public string Name { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public StorePackageKind Kind { get; init; } = StorePackageKind.Software;
    public StorePackageAction Action { get; init; } = StorePackageAction.InstallWithWinget;
    public string? Price { get; init; }
    public string? ImageUrl { get; init; }
    public IReadOnlyList<string> Platforms { get; init; } = [];
    public string? StoreUrl { get; init; }
    public string PlatformSummary => string.Join(" · ", Platforms);
    public string PriceDisplay => !string.IsNullOrWhiteSpace(Price)
        ? Price
        : Action == StorePackageAction.InstallWithWinget ? "Installable" : "View Store";
    public string ActionHint => Action == StorePackageAction.InstallWithWinget
        ? "Install with WinGet"
        : "Official Steam page";
    public string Summary => string.IsNullOrWhiteSpace(Version) ? Source : $"{Source} · {Version}";
}
