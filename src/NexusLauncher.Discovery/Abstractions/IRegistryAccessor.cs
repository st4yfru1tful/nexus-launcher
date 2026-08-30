using Microsoft.Win32;

namespace NexusLauncher.Discovery.Abstractions;

/// <summary>A snapshot of one registry subkey used by installation discovery.</summary>
public sealed record RegistryKeySnapshot(
    string Name,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// Read-only Windows registry abstraction.  Providers never write registry data.
/// </summary>
public interface IRegistryAccessor
{
    object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName);

    IReadOnlyList<RegistryKeySnapshot> EnumerateSubKeys(RegistryHive hive, RegistryView view, string subKeyPath);
}

/// <summary>Production registry reader with graceful behavior outside Windows.</summary>
public sealed class WindowsRegistryAccessor : IRegistryAccessor
{
    public object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            return key?.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    public IReadOnlyList<RegistryKeySnapshot> EnumerateSubKeys(RegistryHive hive, RegistryView view, string subKeyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<RegistryKeySnapshot>();
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
            {
                return Array.Empty<RegistryKeySnapshot>();
            }

            var snapshots = new List<RegistryKeySnapshot>();
            foreach (var name in key.GetSubKeyNames())
            {
                try
                {
                    using var child = key.OpenSubKey(name, writable: false);
                    if (child is null)
                    {
                        continue;
                    }

                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var valueName in child.GetValueNames())
                    {
                        values[valueName] = child.GetValue(
                            valueName,
                            defaultValue: null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames);
                    }

                    snapshots.Add(new RegistryKeySnapshot(name, values));
                }
                catch (IOException)
                {
                    // A single partially removed uninstall key must not abort the scan.
                }
                catch (UnauthorizedAccessException)
                {
                    // Some machine-wide software deliberately restricts its key.
                }
            }

            return snapshots;
        }
        catch (IOException)
        {
            return Array.Empty<RegistryKeySnapshot>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<RegistryKeySnapshot>();
        }
        catch (PlatformNotSupportedException)
        {
            return Array.Empty<RegistryKeySnapshot>();
        }
    }
}

/// <summary>Helpers for reading loosely typed registry values safely.</summary>
public static class RegistryValueExtensions
{
    public static string? GetString(this IReadOnlyDictionary<string, object?> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.TryGetValue(name, out var value)
            ? ToString(value)
            : null;
    }

    public static string? ToString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            string[] values => string.Join(';', values),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public static bool IsEnabled(this IReadOnlyDictionary<string, object?> values, string name)
    {
        var value = values.GetString(name);
        return value is not null &&
               (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
