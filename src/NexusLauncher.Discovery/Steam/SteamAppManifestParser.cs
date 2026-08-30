using System.Globalization;
using NexusLauncher.Discovery.Steam.Vdf;

namespace NexusLauncher.Discovery.Steam;

/// <summary>A normalized subset of a Steam appmanifest (.acf) file.</summary>
public sealed record SteamAppManifest
{
    public uint AppId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string InstallDirectory { get; init; } = string.Empty;

    public int StateFlags { get; init; }

    public string? BuildId { get; init; }

    public string? LastUpdated { get; init; }

    /// <summary>Steam uses the bit value 4 to indicate that the app is fully installed.</summary>
    public bool IsInstalled => (StateFlags & 4) != 0 && !string.IsNullOrWhiteSpace(InstallDirectory);
}

/// <summary>Reads Steam .acf appmanifest files through the shared VDF parser.</summary>
public static class SteamAppManifestParser
{
    public static SteamAppManifest Parse(string text)
    {
        var document = SteamVdfParser.Parse(text);
        var appState = document.Root.GetObject("AppState")
            ?? throw new VdfParseException("An appmanifest must contain an AppState object", 1, 1);

        var appIdText = appState.GetString("appid");
        if (!uint.TryParse(appIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
        {
            throw new VdfParseException("The appmanifest AppState.appid is missing or invalid", 1, 1);
        }

        var name = appState.GetString("name")?.Trim();
        var installDirectory = appState.GetString("installdir")?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new VdfParseException("The appmanifest is missing its name or installdir", 1, 1);
        }

        var stateFlags = int.TryParse(
            appState.GetString("StateFlags"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedStateFlags)
            ? parsedStateFlags
            : 0;

        return new SteamAppManifest
        {
            AppId = appId,
            Name = name,
            InstallDirectory = installDirectory,
            StateFlags = stateFlags,
            BuildId = appState.GetString("buildid"),
            LastUpdated = appState.GetString("LastUpdated"),
        };
    }

    public static bool TryParse(string text, out SteamAppManifest? manifest)
    {
        try
        {
            manifest = Parse(text);
            return true;
        }
        catch (VdfParseException)
        {
            manifest = null;
            return false;
        }
    }
}
