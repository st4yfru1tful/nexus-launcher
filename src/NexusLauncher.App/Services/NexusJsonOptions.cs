using System.Text.Json;

namespace NexusLauncher.App.Services;

internal static class NexusJsonOptions
{
    internal static JsonSerializerOptions Default { get; } = new() { WriteIndented = true };
}
