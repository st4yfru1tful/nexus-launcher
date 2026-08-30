namespace NexusLauncher.App.Models;

/// <summary>
/// Metadata returned from an AI-assisted lookup. This result is intentionally
/// independent from local launch information and can be reviewed before it is
/// applied to a library item.
/// </summary>
public sealed record AiMetadataLookupResult
{
    public string? CanonicalTitle { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public double? Confidence { get; init; }
}
