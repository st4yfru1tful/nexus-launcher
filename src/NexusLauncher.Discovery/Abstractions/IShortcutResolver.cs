using System.Reflection;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Runtime.Versioning;

namespace NexusLauncher.Discovery.Abstractions;

/// <summary>The usable data read from a Windows shell shortcut.</summary>
public sealed record ShortcutTarget
{
    public string? TargetPath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? Description { get; init; }

    public string? IconLocation { get; init; }
}

/// <summary>Resolves a .lnk file without making a discovery provider COM-aware.</summary>
public interface IShortcutResolver
{
    bool TryResolve(string shortcutPath, out ShortcutTarget target);
}

/// <summary>
/// Uses the Windows Script Host shell automation object to resolve .lnk files.
/// This is a local Windows API; no shortcut contents are sent anywhere.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellLinkShortcutResolver : IShortcutResolver
{
    public bool TryResolve(string shortcutPath, out ShortcutTarget target)
    {
        target = new ShortcutTarget();
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(shortcutPath))
        {
            return false;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            shortcut = shell.GetType().InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[] { shortcutPath },
                culture: CultureInfo.InvariantCulture);

            if (shortcut is null)
            {
                return false;
            }

            target = new ShortcutTarget
            {
                TargetPath = ReadComString(shortcut, "TargetPath"),
                Arguments = ReadComString(shortcut, "Arguments"),
                WorkingDirectory = ReadComString(shortcut, "WorkingDirectory"),
                Description = ReadComString(shortcut, "Description"),
                IconLocation = ReadComString(shortcut, "IconLocation"),
            };

            return !string.IsNullOrWhiteSpace(target.TargetPath);
        }
        catch (COMException)
        {
            return false;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (MissingMethodException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static string? ReadComString(object target, string propertyName)
    {
        var value = target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target: target,
            args: null,
            culture: CultureInfo.InvariantCulture);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ReleaseComObject(object? value)
    {
        if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
