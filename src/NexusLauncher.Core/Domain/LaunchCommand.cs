namespace NexusLauncher.Core.Domain;

/// <summary>
/// Describes a safe, user-approved launch target.  A command can use either an
/// executable, a URI understood by an installed launcher, or both.
/// </summary>
public sealed record LaunchCommand
{
    public string? ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? LaunchUri { get; init; }

    public bool IsLaunchable =>
        !string.IsNullOrWhiteSpace(ExecutablePath) ||
        !string.IsNullOrWhiteSpace(LaunchUri);
}
