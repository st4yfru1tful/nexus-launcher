namespace NexusLauncher.App.Infrastructure;

public sealed class NavigationItem(string title, string glyph, object target)
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public object Target { get; } = target;
}
