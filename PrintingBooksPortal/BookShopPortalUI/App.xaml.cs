using System.Threading;
using System.Windows;

namespace BookShopPortalUI;

public partial class App : System.Windows.Application
{
    private static readonly Mutex _mutex = new(true, "BookShopPortalUI_SingleInstance");

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!_mutex.WaitOne(TimeSpan.Zero, true))
        {
            System.Windows.MessageBox.Show("DR Bahig Books Portal is already running.\nCheck the system tray.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        base.OnExit(e);
    }
}
