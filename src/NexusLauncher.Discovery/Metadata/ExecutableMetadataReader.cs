using System.ComponentModel;
using System.Diagnostics;

namespace NexusLauncher.Discovery.Metadata;

/// <summary>Safe metadata that can be extracted locally from an executable.</summary>
public sealed record ExecutableMetadata
{
    public string FilePath { get; init; } = string.Empty;

    public string? FileDescription { get; init; }

    public string? ProductName { get; init; }

    public string? CompanyName { get; init; }

    public string? FileVersion { get; init; }
}

/// <summary>Reads local executable metadata without executing the file.</summary>
public interface IExecutableMetadataReader
{
    ExecutableMetadata Read(string executablePath);
}

/// <summary>Production reader backed by <see cref="FileVersionInfo"/>.</summary>
public sealed class FileVersionExecutableMetadataReader : IExecutableMetadataReader
{
    public ExecutableMetadata Read(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath);
            return new ExecutableMetadata
            {
                FilePath = executablePath,
                FileDescription = EmptyToNull(version.FileDescription),
                ProductName = EmptyToNull(version.ProductName),
                CompanyName = EmptyToNull(version.CompanyName),
                FileVersion = EmptyToNull(version.FileVersion),
            };
        }
        catch (FileNotFoundException)
        {
            return new ExecutableMetadata { FilePath = executablePath };
        }
        catch (IOException)
        {
            return new ExecutableMetadata { FilePath = executablePath };
        }
        catch (Win32Exception)
        {
            return new ExecutableMetadata { FilePath = executablePath };
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
