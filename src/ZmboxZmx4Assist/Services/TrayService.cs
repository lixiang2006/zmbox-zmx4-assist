namespace ZmboxZmx4Assist.Services;

public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private bool _exitRequested;
    public TrayService(System.Windows.Window window, Action shutdown)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示面板", null, (_, _) => { window.Show(); window.WindowState = System.Windows.WindowState.Normal; window.Activate(); });
        menu.Items.Add("退出", null, (_, _) => { _exitRequested = true; shutdown(); });
        _icon = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Zmbox ZMX4 Assist", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => { window.Show(); window.Activate(); };
        window.Closing += (_, e) => { if (!_exitRequested) { e.Cancel = true; window.Hide(); } };
    }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }

    public void ShowNotification(string title, string message) =>
        _icon.ShowBalloonTip(2500, title, message, System.Windows.Forms.ToolTipIcon.Info);

    public void ShowErrorNotification(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, System.Windows.Forms.ToolTipIcon.Error);
}
