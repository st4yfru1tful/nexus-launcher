using System.Text.Json;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class LibraryRepository
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _libraryFile;

    public LibraryRepository()
    {
        NexusPaths.EnsureCreated();
        _libraryFile = NexusPaths.LibraryFile;
    }

    public async Task<List<LibraryItem>> LoadAsync()
    {
        NexusPaths.EnsureCreated();
        if (!File.Exists(_libraryFile))
        {
            return [];
        }

        await Gate.WaitAsync();
        try
        {
            await using var stream = File.OpenRead(_libraryFile);
            return await JsonSerializer.DeserializeAsync<List<LibraryItem>>(stream, NexusJsonOptions.Default) ?? [];
        }
        catch (JsonException)
        {
            var corruptedFile = _libraryFile + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(_libraryFile, corruptedFile, true);
            return [];
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<LibraryItem> items)
    {
        NexusPaths.EnsureCreated();
        await Gate.WaitAsync();
        try
        {
            var temporary = _libraryFile + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, items.OrderBy(item => item.Name), NexusJsonOptions.Default);
            }

            File.Move(temporary, _libraryFile, true);
        }
        finally
        {
            Gate.Release();
        }
    }
}
