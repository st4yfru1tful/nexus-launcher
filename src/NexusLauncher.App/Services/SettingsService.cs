using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class SettingsService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _settingsFile;

    public SettingsService()
    {
        NexusPaths.EnsureCreated();
        _settingsFile = NexusPaths.SettingsFile;
    }

    public async Task<AppSettings> LoadAsync()
    {
        NexusPaths.EnsureCreated();
        if (!File.Exists(_settingsFile))
        {
            return new AppSettings();
        }

        await Gate.WaitAsync();
        try
        {
            await using var stream = File.OpenRead(_settingsFile);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, NexusJsonOptions.Default) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        NexusPaths.EnsureCreated();
        await Gate.WaitAsync();
        try
        {
            var temporary = _settingsFile + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, settings, NexusJsonOptions.Default);
            }

            File.Move(temporary, _settingsFile, true);
        }
        finally
        {
            Gate.Release();
        }
    }
}
