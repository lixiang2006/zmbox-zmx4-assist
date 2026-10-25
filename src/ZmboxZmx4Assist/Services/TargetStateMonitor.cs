using System.Diagnostics;
using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Services;

/// <summary>
/// Production <see cref="IPlaybackTargetGuard"/> for the playback hot path: keeps the answer of the
/// last full readiness check for a short interval and answers the per-event questions from a cheap
/// liveness probe in between. Before this, every event paid twice for a process lookup, window
/// visibility, layout, DPI and foreground queries; a dense macro spent more time in the guard than
/// in the macro itself.
/// </summary>
/// <remarks>
/// The trade-off is detection latency: a moved window is noticed within one full-check interval
/// (100 ms) instead of before the next event. The timeline keeps dispatching meanwhile; the
/// recovery path reacts to a not-ready answer, and a stale ready answer can only hide a layout
/// change for that one interval.
/// </remarks>
internal sealed class TargetStateMonitor : IPlaybackTargetGuard
{
    internal static readonly long DefaultFullCheckIntervalTicks = Stopwatch.Frequency / 10;

    private readonly IPlaybackTargetGuard _guard;
    private readonly ITargetStateProbe _probe;
    private readonly Func<long> _timestamp;
    private readonly long _fullCheckIntervalTicks;
    private TargetReadiness? _cached;
    private IntPtr _cachedWindow;
    private long _cachedAt;

    internal TargetStateMonitor(TargetWindowService targets)
        : this(new TargetWindowGuard(targets), new TargetWindowProbe(targets))
    {
    }

    internal TargetStateMonitor(IPlaybackTargetGuard guard, ITargetStateProbe probe, Func<long>? timestamp = null, long fullCheckIntervalTicks = -1)
    {
        _guard = guard;
        _probe = probe;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _fullCheckIntervalTicks = fullCheckIntervalTicks < 0 ? DefaultFullCheckIntervalTicks : fullCheckIntervalTicks;
    }

    public TargetReadiness Find(MacroDefinition macro, ZmboxTargetSettings target, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        var result = _guard.Find(macro, target, options, manualWindow);
        Remember(result);
        return result;
    }

    public TargetReadiness Check(IntPtr window, MacroDefinition macro, ZmboxTargetSettings target, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        var cheapFailure = CheapFailure(window, options, manualWindow);
        if (cheapFailure is not null) return new TargetReadiness(window, false, cheapFailure);

        if (_cached is { IsReady: true } cached && window == _cachedWindow && _timestamp() - _cachedAt < _fullCheckIntervalTicks)
            return cached;

        var result = _guard.Check(window, macro, target, options, manualWindow);
        Remember(result);
        return result;
    }

    private string? CheapFailure(IntPtr window, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        // A changed handle means the launcher rebuilt its window; that needs the full check, which
        // knows how to rebind inside the process the user locked.
        if (manualWindow is { } binding && window != binding.Window.Handle) return null;
        if (!_probe.IsWindow(window)) return "目标窗口句柄已失效。";
        if (options.Mode == PlaybackMode.ForegroundSystemInput && !_probe.IsForeground(window))
            return "已切离锁定的造梦盒子窗口，前台输入已暂停。";
        if (options.Mode == PlaybackMode.ExperimentalTargetWindow && manualWindow is { MatchesTarget: false })
            return "手动选择的窗口不匹配当前配置档，不能使用后台窗口消息。";
        return null;
    }

    private void Remember(TargetReadiness readiness)
    {
        _cached = readiness;
        _cachedWindow = readiness.Window;
        _cachedAt = _timestamp();
    }
}
