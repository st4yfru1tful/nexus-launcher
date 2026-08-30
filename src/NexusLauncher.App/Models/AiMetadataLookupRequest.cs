namespace NexusLauncher.App.Models;

/// <summary>
/// The complete, deliberately minimal context that may be used to look up
/// metadata for a local library item. Do not add local paths, URIs, arguments,
/// files, or binary content to this contract.
/// </summary>
public sealed record AiMetadataLookupRequest
{
    public required string Title { get; init; }
    public string? Provider { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string? ExecutableFileName { get; init; }
    public string? ParentFolderName { get; init; }
}
