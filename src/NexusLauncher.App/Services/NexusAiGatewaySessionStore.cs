using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

/// <summary>
/// Saves an optional Nexus AI gateway session using Windows DPAPI. The session
/// file is not part of Settings, diagnostics, or a Nexus backup.
/// </summary>
public sealed class NexusAiGatewaySessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NexusLauncher.NexusAiGatewaySession.v1");
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _sessionFile;

    public NexusAiGatewaySessionStore()
        : this(NexusPaths.AiGatewaySessionFile)
    {
    }

    internal NexusAiGatewaySessionStore(string sessionFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFile);
        _sessionFile = Path.GetFullPath(sessionFile);
    }

    public async Task<NexusAiGatewaySession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_sessionFile)) return null;

            try
            {
                var encrypted = await File.ReadAllBytesAsync(_sessionFile, cancellationToken);
                if (encrypted.Length == 0 || encrypted.Length > 128 * 1024)
                {
                    await DeleteUnsafeFileAsync();
                    return null;
                }

                var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    var session = JsonSerializer.Deserialize<NexusAiGatewaySession>(plaintext, NexusJsonOptions.Default);
                    if (session is null)
                    {
                        await DeleteUnsafeFileAsync();
                        return null;
                    }

                    session.EnsureValid();
                    return session;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                await DeleteUnsafeFileAsync();
                return null;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SaveAsync(NexusAiGatewaySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EnsureValid();

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_sessionFile) ?? throw new InvalidOperationException("The Nexus AI session folder is invalid.");
            Directory.CreateDirectory(directory);

            var plaintext = JsonSerializer.SerializeToUtf8Bytes(session, NexusJsonOptions.Default);
            byte[]? encrypted = null;
            try
            {
                encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
                var temporary = _sessionFile + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
                    File.Move(temporary, _sessionFile, true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await DeleteUnsafeFileAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    private Task DeleteUnsafeFileAsync()
    {
        if (File.Exists(_sessionFile)) File.Delete(_sessionFile);
        return Task.CompletedTask;
    }
}
