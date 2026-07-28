using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ZmboxZmx4Assist;

public partial class RecordingIndicatorWindow : System.Windows.Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public RecordingIndicatorWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    public void ShowRecording(string recordHotkey)
    {
        _hideTimer.Stop();
        Panel.Background = System.Windows.Media.Brushes.Firebrick;
        MessageText.Text = $"● 正在录制  ·  {recordHotkey} 结束并保存";
        ShowWithoutActivation();
    }

    public void ShowResult(string message, bool success = true, TimeSpan? duration = null)
    {
        Panel.Background = success ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.Firebrick;
        MessageText.Text = success ? $"✓ {message}" : $"! {message}";
        ShowWithoutActivation();
        _hideTimer.Stop();
        _hideTimer.Interval = duration ?? TimeSpan.FromSeconds(3);
        _hideTimer.Start();
    }

    private void ShowWithoutActivation()
    {
        Left = Math.Max(16, SystemParameters.WorkArea.Right - Width - 24);
        Top = Math.Max(16, SystemParameters.WorkArea.Top + 24);
        if (!IsVisible) Show();
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
