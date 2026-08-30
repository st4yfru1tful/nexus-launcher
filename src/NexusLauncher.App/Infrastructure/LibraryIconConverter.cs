using System.Globalization;
using System.Windows.Data;
using NexusLauncher.App.Services;

namespace NexusLauncher.App.Infrastructure;

/// <summary>Resolves IconPath and ExecutablePath values to one safe local icon.</summary>
public sealed class LibraryIconConverter : IMultiValueConverter
{
    private readonly LibraryIconService _iconService;

    public LibraryIconConverter()
        : this(LibraryIconService.Shared)
    {
    }

    internal LibraryIconConverter(LibraryIconService iconService)
    {
        _iconService = iconService ?? throw new ArgumentNullException(nameof(iconService));
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var iconPath = values.ElementAtOrDefault(0) as string;
        var executablePath = values.ElementAtOrDefault(1) as string;
        return _iconService.GetIcon(iconPath, executablePath)!;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
