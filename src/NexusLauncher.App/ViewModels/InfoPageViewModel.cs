namespace NexusLauncher.App.ViewModels;

public sealed class InfoPageViewModel(string title, string subtitle, string heading, string body, string glyph) : PageViewModel(title, subtitle)
{
    public string Heading { get; } = heading;
    public string Body { get; } = body;
    public string Glyph { get; } = glyph;
}
