using System.Security.Cryptography;
using System.Text;

namespace NexusLauncher.App.Services;

/// <summary>
/// RFC 7636 helpers for a public desktop OAuth client. The verifier and state
/// exist only for one browser sign-in attempt and are never persisted.
/// </summary>
internal static class OAuthPkce
{
    private const string UnreservedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";

    internal static string CreateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    internal static string CreateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    internal static string CreateCodeChallenge(string verifier)
    {
        if (!IsValidCodeVerifier(verifier))
        {
            throw new ArgumentException("The OAuth PKCE verifier is invalid.", nameof(verifier));
        }

        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    internal static bool IsValidCodeVerifier(string? value) =>
        value is { Length: >= 43 and <= 128 } && value.All(character => UnreservedCharacters.Contains(character, StringComparison.Ordinal));

    internal static bool IsValidState(string? value) =>
        value is { Length: >= 32 and <= 128 } && value.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    internal static bool StateMatches(string expectedState, string? receivedState)
    {
        if (receivedState is null || !IsValidState(expectedState) || !IsValidState(receivedState) || expectedState.Length != receivedState.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedState),
            Encoding.ASCII.GetBytes(receivedState));
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
