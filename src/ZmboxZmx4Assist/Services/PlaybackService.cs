using System.Diagnostics;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Interop;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

public sealed class PlaybackService : IPlaybackRoundExecutor
{
    private const int ForegroundRecoverySeconds = 5;
    private const int BackgroundRecoverySeconds = 15;

    /// <summary>
    /// Wake-ups shorter than this are the normal spin margin. Longer ones are a real stall (a GC
    /// pause, a busy machine, a slow frame) and are absorbed into the timeline instead of letting
    /// the remaining gaps collapse into one burst.
    /// </summary>
    internal static readonly long OvershootToleranceTicks = Stopwatch.Frequency / 4_000;

    private readonly IInputSink _input;
    private readonly IPlaybackTargetGuard _targetGuard;
    private readonly HashSet<int> _pressedKeys = [];
    private readonly Dictionary<MouseButtonKind, RecordedEvent> _pressedButtons = [];
    private CancellationTokenSource? _running;

    public PlaybackService(TargetWindowService targets, IInputSink? input = null, IPlaybackTargetGuard? targetGuard = null)
    {
        _input = input ?? new NativeInputSink();
        _targetGuard = targetGuard ?? new TargetStateMonitor(targets);
    }

    public bool IsRunning => _running is not null;
    public event Action<string>? StatusChanged;
    public event Action? StateChanged;
    public event Action<PlaybackStopResult>? PlaybackStopped;

    public async Task PlayAsync(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow = null)
    {
        await PlayCoreAsync(macro, profile, options, manualWindow, CancellationToken.None, reportStop: true, reportCompletion: true);
    }

    public async Task<PlaybackRunResult> PlaySingleIterationAsync(
        MacroDefinition macro,
        TargetProfile profile,
        PlaybackOptions options,
        ManualWindowBinding manualWindow,
        CancellationToken cancellationToken)
    {
        var singleIteration = options with
        {
            RepeatCount = 1,
            InfiniteLoop = false,
            InterIterationDelaySeconds = 0,
            CooldownEveryIterations = 1,
            CooldownSeconds = 0
        };
        return await PlayCoreAsync(macro, profile, singleIteration, manualWindow, cancellationToken, reportStop: false, reportCompletion: false);
    }

    /// <summary>
    /// Validates the request synchronously, so callers keep receiving the same argument errors,
    /// publishes the running state immediately, and hands the timeline to a pool thread.
    /// </summary>
    private Task<PlaybackRunResult> PlayCoreAsync(
        MacroDefinition macro,
        TargetProfile profile,
        PlaybackOptions options,
        ManualWindowBinding? manualWindow,
        CancellationToken cancellationToken,
        bool reportStop,
        bool reportCompletion)
    {
        if (_running is not null) throw new InvalidOperationException("已有宏正在回放。");
        // Macros are validated once when they are loaded or saved, so a restart session does not
        // re-sort the whole event list at every iteration boundary; an empty macro is still
        // rejected here because it cannot produce any input.
        if (macro.Events.Count == 0) throw new InvalidOperationException("宏没有可回放的输入事件。");
        if (macro.DisplayLayout is null) throw new InvalidOperationException("宏缺少录制时的显示布局，不能安全回放。");
        if (options.SpeedMultiplier is < 0.90 or > 1.10) throw new InvalidOperationException("速度仅可在 0.90x 到 1.10x 之间调整。");
        if (options.InterIterationDelaySeconds is < 0 or > 120) throw new InvalidOperationException("每轮间隔必须在 0 到 120 秒之间。");
        if (options.CooldownEveryIterations is < 1 or > 1000) throw new InvalidOperationException("周期冷却轮数必须在 1 到 1000 之间。");
        if (options.CooldownSeconds is < 0 or > 900) throw new InvalidOperationException("周期冷却等待必须在 0 到 900 秒之间。");

        _running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StateChanged?.Invoke();
        // The timeline busy-spins the last milliseconds of every gap and then calls SendInput. On
        // the Dispatcher thread that starved the window's own rendering and input handling, which
        // is what made long sessions look stuck; the status/state events marshal themselves back.
        return Task.Run(() => RunCoreAsync(macro, profile, options, manualWindow, reportStop, reportCompletion));
    }

    private async Task<PlaybackRunResult> RunCoreAsync(
        MacroDefinition macro,
        TargetProfile profile,
        PlaybackOptions options,
        ManualWindowBinding? manualWindow,
        bool reportStop,
        bool reportCompletion)
    {
        var token = _running!.Token;
        var window = IntPtr.Zero;
        using var timer = HighResolutionTimer.Acquire();
        try
        {
            token.ThrowIfCancellationRequested();
            window = EnsureTargetReady(macro, profile, options, manualWindow);
            // 后台通道靠窗口消息投递；启动前先试一次，把权限/完整性拦截说清楚，而不是在宏跑到一半时中断。
            if (options.Mode == PlaybackMode.ExperimentalTargetWindow && !_input.TryProbeBackgroundChannel(window, out var probeFailure))
                throw new InvalidOperationException(probeFailure);
            var iteration = 0;
            do
            {
                iteration++;
                StatusChanged?.Invoke($"正在回放第 {iteration} 轮…");
                window = await PlayOnceAsync(macro.Events, window, macro, profile, options, manualWindow, token);
                ReleaseAll(window, options.Mode);
                var hasNextIteration = options.InfiniteLoop || iteration < Math.Max(1, options.RepeatCount);
                if (hasNextIteration && options.InterIterationDelaySeconds > 0)
                    await WaitBetweenIterationsAsync(iteration, options.InterIterationDelaySeconds, token);
                if (options.ShouldCooldownAfter(iteration, hasNextIteration))
                {
                    await WaitCooldownAsync(iteration, options.CooldownSeconds, token);
                    window = await RecoverFoundWindowAsync(macro, profile, options, manualWindow, token);
                }
            }
            while (options.InfiniteLoop || iteration < Math.Max(1, options.RepeatCount));
            if (reportCompletion) StatusChanged?.Invoke("回放完成。");
            return PlaybackRunResult.Completed;
        }
        catch (OperationCanceledException ex)
        {
            var userRequested = _running!.IsCancellationRequested;
            var reason = userRequested || string.IsNullOrWhiteSpace(ex.Message)
                ? "已请求停止。"
                : ex.Message;
            StatusChanged?.Invoke($"回放已停止：{reason}");
            var stop = new PlaybackStopResult(userRequested ? PlaybackStopKind.UserRequested : PlaybackStopKind.RecoveryTimedOut, reason);
            if (reportStop) PlaybackStopped?.Invoke(stop);
            return new PlaybackRunResult(false, stop);
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? "发生未知输入错误。" : ex.Message;
            StatusChanged?.Invoke($"回放已停止：{reason}");
            var stop = new PlaybackStopResult(PlaybackStopKind.InputFailure, reason);
            if (reportStop) PlaybackStopped?.Invoke(stop);
            return new PlaybackRunResult(false, stop);
        }
        finally
        {
            ReleaseAll(window, options.Mode);
            _running?.Dispose();
            _running = null;
            StateChanged?.Invoke();
        }
    }

    public void EmergencyStop()
    {
        try
        {
            _running?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 回放线程已经在收尾并释放了取消源；此时停止请求已经是既成事实。
        }
    }

    private async Task WaitBetweenIterationsAsync(int completedIteration, int seconds, CancellationToken token)
    {
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            token.ThrowIfCancellationRequested();
            StatusChanged?.Invoke($"第 {completedIteration} 轮结束，等待加载完成：{remaining} 秒…");
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
    }

    private async Task WaitCooldownAsync(int completedIteration, int seconds, CancellationToken token)
    {
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            token.ThrowIfCancellationRequested();
            StatusChanged?.Invoke($"第 {completedIteration} 轮结束，已释放输入，周期冷却：{remaining} 秒…");
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        StatusChanged?.Invoke("周期冷却完成，正在重新检查目标窗口…");
    }

    private IntPtr EnsureTargetReady(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        var readiness = _targetGuard.Find(macro, profile, options, manualWindow);
        if (!readiness.IsReady) throw new InvalidOperationException(readiness.Reason);
        return readiness.Window;
    }

    private async Task<IntPtr> RecoverFoundWindowAsync(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow, CancellationToken token)
    {
        var readiness = _targetGuard.Find(macro, profile, options, manualWindow);
        if (readiness.IsReady) return readiness.Window;
        return (await RecoverWindowAsync(macro, profile, options, manualWindow, readiness.Reason, HeldInputState.Empty, token)).Window;
    }

    private async Task<IntPtr> PlayOnceAsync(IReadOnlyList<RecordedEvent> events, IntPtr window, MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        // The virtual desktop only changes when the display layout does, which playback already
        // treats as a stop condition, so resolve it once per round instead of per mouse event.
        var bounds = NativeMethods.GetVirtualScreenBounds();
        foreach (var e in events)
        {
            token.ThrowIfCancellationRequested();
            var beforeWait = await RecoverCurrentWindowIfNeededAsync(window, macro, profile, options, manualWindow, token);
            window = beforeWait.Window;
            started += beforeWait.PauseTicks;

            var dueTicks = DueTicks(e.OffsetMicroseconds / options.SpeedMultiplier);
            while (true)
            {
                await WaitUntilAsync(started, dueTicks, token);
                var beforeDispatch = await RecoverCurrentWindowIfNeededAsync(window, macro, profile, options, manualWindow, token);
                window = beforeDispatch.Window;
                if (beforeDispatch.PauseTicks == 0) break;
                started += beforeDispatch.PauseTicks;
            }

            // A late wake-up used to leave the timeline reference behind, so every event that
            // followed fired back-to-back - the "断触" symptom. Absorb the stall into the
            // reference instead, which keeps the recorded intervals intact.
            started += OvershootTicks(started, dueTicks, Stopwatch.GetTimestamp());
            Dispatch(window, e, options.Mode, bounds);
        }
        return window;
    }

    private async Task<RecoveryResult> RecoverCurrentWindowIfNeededAsync(IntPtr window, MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow, CancellationToken token)
    {
        var readiness = _targetGuard.Check(window, macro, profile, options, manualWindow);
        if (readiness.IsReady) return new RecoveryResult(window, 0);

        var held = SnapshotHeldInputs();
        ReleaseAll(window, options.Mode);
        return await RecoverWindowAsync(macro, profile, options, manualWindow, readiness.Reason, held, token);
    }

    private async Task<RecoveryResult> RecoverWindowAsync(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow, string initialReason, HeldInputState held, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var latestReason = initialReason;
        var recoverySeconds = options.Mode == PlaybackMode.ExperimentalTargetWindow
            ? BackgroundRecoverySeconds
            : ForegroundRecoverySeconds;
        var elapsedSeconds = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var readiness = _targetGuard.Find(macro, profile, options, manualWindow);
            if (readiness.IsReady)
            {
                RestoreHeldInputs(readiness.Window, options.Mode, held);
                StatusChanged?.Invoke("目标窗口已恢复，继续当前回放。");
                return new RecoveryResult(readiness.Window, Stopwatch.GetTimestamp() - started);
            }

            latestReason = readiness.Reason;
            var waitForLockedProcess = options.Mode == PlaybackMode.ExperimentalTargetWindow &&
                manualWindow is not null &&
                readiness.IsTransient;
            if (!waitForLockedProcess && elapsedSeconds >= recoverySeconds)
                throw new OperationCanceledException($"{latestReason} 已等待 {recoverySeconds} 秒仍未恢复。");

            var status = waitForLockedProcess
                ? $"回放已暂停：{latestReason} 正在等待已锁定的造梦盒子窗口恢复（已等待 {elapsedSeconds} 秒；按 F12 停止）…"
                : $"回放已暂停：{latestReason} 正在尝试恢复（剩余 {recoverySeconds - elapsedSeconds} 秒）…";
            StatusChanged?.Invoke(status);
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            elapsedSeconds++;
        }
    }

    internal static long DueTicks(double dueMicroseconds) =>
        (long)(dueMicroseconds * Stopwatch.Frequency / 1_000_000d);

    internal static long OvershootTicks(long started, long dueTicks, long nowTicks)
    {
        var late = nowTicks - (started + dueTicks);
        return late > OvershootToleranceTicks ? late : 0;
    }

    private static async Task WaitUntilAsync(long start, long dueTicks, CancellationToken token)
    {
        while (true)
        {
            var remaining = dueTicks - (Stopwatch.GetTimestamp() - start);
            if (remaining <= 0) return;
            var remainingMs = remaining * 1000d / Stopwatch.Frequency;
            if (remainingMs > 3) await Task.Delay(TimeSpan.FromMilliseconds(remainingMs - 2), token);
            else Thread.SpinWait(80);
        }
    }

    private void Dispatch(IntPtr window, RecordedEvent e, PlaybackMode mode, VirtualScreenBounds bounds)
    {
        _input.Dispatch(window, e, mode, bounds);
        Track(e);
    }

    private void Track(RecordedEvent e)
    {
        if (e.Kind == InputEventKind.KeyDown) _pressedKeys.Add(e.VirtualKey);
        if (e.Kind == InputEventKind.KeyUp) _pressedKeys.Remove(e.VirtualKey);
        if (e.Kind == InputEventKind.MouseDown) _pressedButtons[e.Button] = e;
        if (e.Kind == InputEventKind.MouseUp) _pressedButtons.Remove(e.Button);
    }

    private HeldInputState SnapshotHeldInputs() => new(_pressedKeys.ToArray(), _pressedButtons.Values.ToArray());

    private void RestoreHeldInputs(IntPtr window, PlaybackMode mode, HeldInputState held)
    {
        var bounds = NativeMethods.GetVirtualScreenBounds();
        foreach (var key in held.Keys) Dispatch(window, new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = key }, mode, bounds);
        foreach (var button in held.Buttons) Dispatch(window, button with { Kind = InputEventKind.MouseDown }, mode, bounds);
    }

    private void ReleaseAll(IntPtr window, PlaybackMode mode)
    {
        var held = SnapshotHeldInputs();
        _pressedKeys.Clear();
        _pressedButtons.Clear();
        foreach (var key in held.Keys) DispatchRelease(window, new RecordedEvent { Kind = InputEventKind.KeyUp, VirtualKey = key }, mode);
        foreach (var button in held.Buttons) DispatchRelease(window, button with { Kind = InputEventKind.MouseUp }, mode);
    }

    private void DispatchRelease(IntPtr window, RecordedEvent e, PlaybackMode mode)
    {
        try
        {
            _input.Release(window, e, mode);
        }
        catch
        {
            // 紧急释放是最佳努力操作；不能让释放失败遮蔽原始的停止原因。
        }
    }

    private sealed record RecoveryResult(IntPtr Window, long PauseTicks);
    private sealed record HeldInputState(int[] Keys, RecordedEvent[] Buttons)
    {
        public static HeldInputState Empty { get; } = new([], []);
    }
}
