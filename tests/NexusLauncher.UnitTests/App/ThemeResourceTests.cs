using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using NexusLauncher.App.ViewModels;

namespace NexusLauncher.UnitTests.App;

public sealed class ThemeResourceTests
{
    [Fact]
    public void Applying_a_theme_replaces_a_frozen_brush_resource()
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            Application? application = null;
            try
            {
                application = new Application();
                var frozenBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x1C));
                frozenBrush.Freeze();
                application.Resources["NexusBackground"] = frozenBrush;

                var setBrush = typeof(ShellViewModel).GetMethod("SetBrush", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(setBrush);
                setBrush.Invoke(null, ["NexusBackground", "#F4F6FA"]);

                var replacement = Assert.IsType<SolidColorBrush>(application.Resources["NexusBackground"]);
                Assert.NotSame(frozenBrush, replacement);
                Assert.Equal(Color.FromRgb(0xF4, 0xF6, 0xFA), replacement.Color);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                application?.Shutdown();
            }
        });

        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "The WPF resource test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
