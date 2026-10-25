using ZmboxZmx4Assist.Services;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;

namespace ZmboxZmx4Assist;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private bool _handlingUnhandledException;

    public App()
    {
        // A WPF app with no dispatcher guard terminates on the first malformed file or failed
        // hotkey path. Log it, release held inputs, tell the user, and keep the session alive.
        DispatcherUnhandledException += (_, e) => HandleUnhandledException(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var chineseCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = chineseCulture;
        CultureInfo.DefaultThreadCurrentUICulture = chineseCulture;
        Thread.CurrentThread.CurrentCulture = chineseCulture;
        Thread.CurrentThread.CurrentUICulture = chineseCulture;

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        MainWindow = window;
        _tray = new TrayService(window, Shutdown);
        window.SetNotifier(_tray.ShowNotification, _tray.ShowErrorNotification);
        window.Show();
    }

    private void HandleUnhandledException(System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (_handlingUnhandledException) return; // Never recurse: let the process terminate instead.

        _handlingUnhandledException = true;
        try
        {
            try
            {
                TryAppendCrashLog(e.Exception);
                if (MainWindow is MainWindow window) window.EmergencyStopForCrash();
            }
            catch
            {
                // Recovery is best-effort: a second failure must never escape this handler.
            }

            try
            {
                System.Windows.MessageBox.Show(
                    $"程序遇到了未预期的错误，已尝试释放按键并继续运行：\n{e.Exception.Message}\n\n详细信息已写入 %LOCALAPPDATA%\\ZmboxZmx4Assist\\crash.log。",
                    "造梦盒子·造梦西游四辅助工具",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // A failing message box must not become a second fatal exception.
            }
        }
        finally
        {
            _handlingUnhandledException = false;
            e.Handled = true;
        }
    }

    private static void TryAppendCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZmboxZmx4Assist");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Diagnostics are best-effort; never let logging mask the original failure.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
