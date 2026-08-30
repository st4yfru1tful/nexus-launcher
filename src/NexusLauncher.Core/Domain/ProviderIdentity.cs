namespace NexusLauncher.Core.Domain;

/// <summary>
/// An identity issued by an installation provider.  The provider and key are
/// deliberately part of the identity so, for example, a Steam app id can never
/// be confused with a GOG product id that happens to have the same value.
/// </summary>
public sealed record ProviderIdentity(string Provider, string Key, string Value)
{
    /// <summary>Gets whether this identity can safely be used for matching.</summary>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Provider) &&
        !string.IsNullOrWhiteSpace(Key) &&
        !string.IsNullOrWhiteSpace(Value);

    /// <summary>Gets a case-insensitive stable key used by duplicate matching.</summary>
    public string CanonicalKey => string.Concat(
        NormalizePart(Provider),
        "\u001f",
        NormalizePart(Key),
        "\u001f",
        NormalizePart(Value));

    private static string NormalizePart(string value) => value.Trim().ToUpperInvariant();
}
