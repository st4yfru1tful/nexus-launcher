using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class LibraryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<List<LibraryItem>> LoadAsync()
    {
        NexusPaths.EnsureCreated();
        if (!File.Exists(NexusPaths.LibraryFile))
        {
            return [];
        }

        await _gate.WaitAsync();
        try
        {
            await using var stream = File.OpenRead(NexusPaths.LibraryFile);
            return await JsonSerializer.DeserializeAsync<List<LibraryItem>>(stream, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            var corruptedFile = NexusPaths.LibraryFile + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(NexusPaths.LibraryFile, corruptedFile, true);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<LibraryItem> items)
    {
        NexusPaths.EnsureCreated();
        await _gate.WaitAsync();
        try
        {
            var temporary = NexusPaths.LibraryFile + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, items.OrderBy(item => item.Name), JsonOptions);
            }

            File.Move(temporary, NexusPaths.LibraryFile, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
