using System.Windows;
using System.Windows.Threading;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist;

public partial class ForegroundWindowCaptureDialog : Window
{
    private const int CountdownSeconds = 3;
    private readonly TargetWindowService _targets;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remainingSeconds = CountdownSeconds;

    public ForegroundWindowCaptureDialog(TargetWindowService targets)
    {
        InitializeComponent();
        _targets = targets;
        Loaded += (_, _) => StartCountdown();
        Closed += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => CaptureTick();
    }

    public WindowCandidate? CapturedWindow { get; private set; }

    private void StartCountdown()
    {
        CapturedWindow = null;
        _remainingSeconds = CountdownSeconds;
        CountdownText.Text = _remainingSeconds.ToString();
        CaptureHintText.Text = "倒计时结束时会锁定当前前台窗口。";
        RetryButton.Visibility = Visibility.Collapsed;
        _timer.Start();
    }

    private void CaptureTick()
    {
        _remainingSeconds--;
        CountdownText.Text = Math.Max(_remainingSeconds, 0).ToString();
        if (_remainingSeconds > 0) return;

        _timer.Stop();
        CapturedWindow = _targets.CaptureForegroundZmboxWindow();
        if (CapturedWindow is not null)
        {
            DialogResult = true;
            return;
        }

        CaptureHintText.Text = "没有捕获到造梦盒子窗口。请点击造梦盒子《造梦西游四》后重新倒计时。";
        RetryButton.Visibility = Visibility.Visible;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => StartCountdown();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
