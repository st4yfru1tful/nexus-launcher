using System.Windows.Input;

namespace NexusLauncher.App.Infrastructure;

public sealed class PaletteCommand(string name, string description, ICommand command)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public ICommand Command { get; } = command;
}
