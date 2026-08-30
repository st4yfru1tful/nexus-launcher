using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppSettings> LoadAsync()
    {
        NexusPaths.EnsureCreated();
        if (!File.Exists(NexusPaths.SettingsFile))
        {
            return new AppSettings();
        }

        await _gate.WaitAsync();
        try
        {
            await using var stream = File.OpenRead(NexusPaths.SettingsFile);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        NexusPaths.EnsureCreated();
        await _gate.WaitAsync();
        try
        {
            var temporary = NexusPaths.SettingsFile + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
            }

            File.Move(temporary, NexusPaths.SettingsFile, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
