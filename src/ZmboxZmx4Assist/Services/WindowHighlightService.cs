using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Interop;

namespace ZmboxZmx4Assist.Services;

public sealed class WindowHighlightService : IDisposable
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(50);
    private readonly TargetWindowService _targets;
    private readonly List<IntPtr> _bars = [];
    private bool _disposed;

    public WindowHighlightService(TargetWindowService targets) => _targets = targets;

    public async Task<WindowHighlightResult> ShowAsync(WindowCandidate target, CancellationToken cancellationToken = default)
    {
        Stop();
        if (_targets.ValidateSelectedWindow(target) is not null || NativeMethods.IsIconic(target.Handle))
            return WindowHighlightResult.Failed("锁定的窗口已失效、隐藏或最小化。");
        if (!NativeMethods.TryGetVisualBounds(target.Handle, out var bounds))
            return WindowHighlightResult.Failed("无法读取锁定窗口的物理边界。");

        try
        {
            PositionBars(bounds);
            var until = DateTime.UtcNow + Duration;
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(RefreshInterval, cancellationToken);
                if (_targets.ValidateSelectedWindow(target) is not null || NativeMethods.IsIconic(target.Handle))
                    return WindowHighlightResult.Failed("锁定窗口在提示期间失效、隐藏或最小化。");
                if (!NativeMethods.TryGetVisualBounds(target.Handle, out bounds))
                    return WindowHighlightResult.Failed("锁定窗口的物理边界已不可用。");
                PositionBars(bounds);
            }
            return WindowHighlightResult.Completed;
        }
        catch (OperationCanceledException)
        {
            return WindowHighlightResult.Failed("锁定提示已取消。");
        }
        finally
        {
            Stop();
        }
    }

    public void Stop()
    {
        foreach (var bar in _bars) NativeMethods.DestroyWindow(bar);
        _bars.Clear();
    }

    private void PositionBars(PhysicalRect target)
    {
        var rectangles = WindowHighlightGeometry.CreateBars(target);
        while (_bars.Count < rectangles.Count)
        {
            var bar = NativeMethods.CreateHighlightBar();
            if (bar == IntPtr.Zero) throw new InvalidOperationException("无法创建锁定边框窗口。");
            _bars.Add(bar);
        }
        for (var i = 0; i < rectangles.Count; i++) NativeMethods.PositionHighlightBar(_bars[i], rectangles[i]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

public sealed record WindowHighlightResult(bool Succeeded, string Message)
{
    public static WindowHighlightResult Completed { get; } = new(true, "已显示锁定窗口边框。");
    public static WindowHighlightResult Failed(string message) => new(false, message);
}
