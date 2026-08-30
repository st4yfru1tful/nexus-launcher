namespace NexusLauncher.App.Models;

public sealed class StorePackage
{
    public string Name { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Summary => string.IsNullOrWhiteSpace(Version) ? Source : $"{Source} · {Version}";
}
