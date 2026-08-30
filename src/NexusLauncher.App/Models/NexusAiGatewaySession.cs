namespace NexusLauncher.App.Models;

/// <summary>
/// A per-user session issued by the developer-owned Nexus AI gateway identity
/// provider. This is not an OpenAI account token and is only persisted through
/// <c>NexusAiGatewaySessionStore</c>'s DPAPI protection.
/// </summary>
public sealed record NexusAiGatewaySession
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public string? Scope { get; init; }

    public bool IsAccessTokenUsable(DateTimeOffset nowUtc, TimeSpan? safetyWindow = null)
    {
        var window = safetyWindow ?? TimeSpan.FromMinutes(1);
        return ExpiresAtUtc > nowUtc.Add(window);
    }

    internal void EnsureValid()
    {
        if (!IsSafeToken(AccessToken) || !IsSafeToken(RefreshToken))
        {
            throw new InvalidDataException("The Nexus AI gateway session is invalid.");
        }

        if (!string.Equals(TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            ExpiresAtUtc < DateTimeOffset.UnixEpoch ||
            ExpiresAtUtc > DateTimeOffset.UtcNow.AddYears(10) ||
            (Scope is not null && !IsSafeScope(Scope)))
        {
            throw new InvalidDataException("The Nexus AI gateway session is invalid.");
        }
    }

    private static bool IsSafeToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 16 * 1024 &&
        value.All(character => !char.IsControl(character));

    private static bool IsSafeScope(string value) =>
        value.Length <= 1024 && value.All(character => !char.IsControl(character));
}
