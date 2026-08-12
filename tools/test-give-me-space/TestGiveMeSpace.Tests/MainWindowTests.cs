using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using TestGiveMeSpace.Core;
using TestGiveMeSpace.Server;

namespace TestGiveMeSpace.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public void Countdown_text_update_keeps_close_menu_visible()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                string directory = Path.Combine(
                    Path.GetTempPath(),
                    "tgms-window-tests",
                    Guid.NewGuid().ToString("N"));
                MainWindow window = new(new SettingsStore(directory));
                Border closeMenu = Assert.IsType<Border>(window.FindName("CloseMenu"));
                closeMenu.Visibility = Visibility.Visible;

                window.SetDisplayText("Тест забирает управление, клик для отмены: 9");

                Assert.Equal(Visibility.Visible, closeMenu.Visibility);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF-проверка не завершилась за 10 секунд.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
