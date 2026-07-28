using ZmboxZmx4Assist.Services;
using System.Windows;

namespace ZmboxZmx4Assist;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        MainWindow = window;
        _tray = new TrayService(window, Shutdown);
        window.SetNotifier(_tray.ShowNotification, _tray.ShowErrorNotification);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
