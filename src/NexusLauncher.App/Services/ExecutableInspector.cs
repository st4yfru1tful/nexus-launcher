using System.Diagnostics;
using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed class ExecutableInspector
{
    private static readonly string[] IgnoredTerms =
    [
        "uninstall", "unins", "crashreport", "crashpad", "helper", "updater", "update", "setup", "install", "redist",
        "vc_redist", "eac", "easyanticheat", "battleye", "service", "bootstrap"
    ];

    public bool IsLikelyLaunchable(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name) || IgnoredTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    public LibraryItem CreateFromExecutable(string executablePath, bool isManual = false)
    {
        var info = TryReadVersionInfo(executablePath);
        var name = FirstNonBlank(info?.ProductName, info?.FileDescription, Path.GetFileNameWithoutExtension(executablePath))!;
        var category = Classify(name, info?.CompanyName, Path.GetDirectoryName(executablePath));
        return new LibraryItem
        {
            Name = name.Trim(),
            Category = category,
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            InstallPath = Path.GetDirectoryName(executablePath),
            Provider = isManual ? "Manual" : "Windows",
            Publisher = BlankToNull(info?.CompanyName),
            Version = BlankToNull(info?.ProductVersion),
            Description = BlankToNull(info?.FileDescription),
            IsManual = isManual
        };
    }

    public LibraryCategory Classify(string? name, string? publisher, string? path)
    {
        var text = $"{name} {publisher} {path}".ToLowerInvariant();
        if (text.Contains("steam") || text.Contains("epic games") || text.Contains("gog galaxy") || text.Contains("ubisoft connect") || text.Contains("ea app") || text.Contains("battle.net"))
        {
            return LibraryCategory.Launcher;
        }

        if (text.Contains("visual studio") || text.Contains("rider") || text.Contains("pycharm") || text.Contains("intellij") || text.Contains("git") || text.Contains("docker") || text.Contains("postman"))
        {
            return LibraryCategory.DevelopmentTool;
        }

        if (text.Contains("blender") || text.Contains("obs") || text.Contains("audacity") || text.Contains("davinci") || text.Contains("adobe") || text.Contains("vlc"))
        {
            return LibraryCategory.MediaSoftware;
        }

        if (text.Contains("discord") || text.Contains("notepad") || text.Contains("browser") || text.Contains("firefox") || text.Contains("chrome") || text.Contains("office"))
        {
            return LibraryCategory.Application;
        }

        return LibraryCategory.Unknown;
    }

    private static FileVersionInfo? TryReadVersionInfo(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path); }
        catch { return null; }
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
