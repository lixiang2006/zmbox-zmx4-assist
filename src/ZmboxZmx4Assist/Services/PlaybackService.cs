using System.Diagnostics;
using System.Runtime.InteropServices;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Interop;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

public sealed class PlaybackService
{
    private const int ForegroundRecoverySeconds = 5;
    private const int BackgroundRecoverySeconds = 15;
    private readonly TargetWindowService _targets;
    private readonly HashSet<int> _pressedKeys = [];
    private readonly Dictionary<MouseButtonKind, RecordedEvent> _pressedButtons = [];
    private CancellationTokenSource? _running;

    public PlaybackService(TargetWindowService targets) => _targets = targets;
    public bool IsRunning => _running is not null;
    public event Action<string>? StatusChanged;
    public event Action? StateChanged;
    public event Action<PlaybackStopResult>? PlaybackStopped;

    public async Task PlayAsync(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow = null)
    {
        if (_running is not null) throw new InvalidOperationException("已有宏正在回放。");
        var error = MacroValidator.Validate(macro);
        if (error is not null) throw new InvalidOperationException(error);
        if (macro.DisplayLayout is null) throw new InvalidOperationException("宏缺少录制时的显示布局，不能安全回放。");
        if (options.SpeedMultiplier is < 0.90 or > 1.10) throw new InvalidOperationException("速度仅可在 0.90x 到 1.10x 之间调整。");
        if (options.InterIterationDelaySeconds is < 0 or > 120) throw new InvalidOperationException("每轮间隔必须在 0 到 120 秒之间。");
        if (options.CooldownEveryIterations is < 1 or > 1000) throw new InvalidOperationException("周期冷却轮数必须在 1 到 1000 之间。");
        if (options.CooldownSeconds is < 0 or > 900) throw new InvalidOperationException("周期冷却等待必须在 0 到 900 秒之间。");

        var window = EnsureTargetReady(macro, profile, options, manualWindow);
        _running = new CancellationTokenSource();
        StateChanged?.Invoke();
        try
        {
            var iteration = 0;
            do
            {
                iteration++;
                StatusChanged?.Invoke($"正在回放第 {iteration} 轮…");
                window = await PlayOnceAsync(macro.Events, window, macro, profile, options, manualWindow, _running.Token);
                ReleaseAll(window, options.Mode);
                var hasNextIteration = options.InfiniteLoop || iteration < Math.Max(1, options.RepeatCount);
                if (hasNextIteration && options.InterIterationDelaySeconds > 0)
                    await WaitBetweenIterationsAsync(iteration, options.InterIterationDelaySeconds, _running.Token);
                if (options.ShouldCooldownAfter(iteration, hasNextIteration))
                {
                    await WaitCooldownAsync(iteration, options.CooldownSeconds, _running.Token);
                    window = await RecoverFoundWindowAsync(macro, profile, options, manualWindow, _running.Token);
                }
            }
            while (options.InfiniteLoop || iteration < Math.Max(1, options.RepeatCount));
            StatusChanged?.Invoke("回放完成。");
        }
        catch (OperationCanceledException ex)
        {
            var userRequested = _running.IsCancellationRequested;
            var reason = userRequested || string.IsNullOrWhiteSpace(ex.Message)
                ? "已请求停止。"
                : ex.Message;
            StatusChanged?.Invoke($"回放已停止：{reason}");
            PlaybackStopped?.Invoke(new PlaybackStopResult(userRequested ? PlaybackStopKind.UserRequested : PlaybackStopKind.RecoveryTimedOut, reason));
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? "发生未知输入错误。" : ex.Message;
            StatusChanged?.Invoke($"回放已停止：{reason}");
            PlaybackStopped?.Invoke(new PlaybackStopResult(PlaybackStopKind.InputFailure, reason));
        }
        finally
        {
            ReleaseAll(window, options.Mode);
            _running.Dispose();
            _running = null;
            StateChanged?.Invoke();
        }
    }

    public void EmergencyStop() => _running?.Cancel();

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
        var readiness = FindTargetReadiness(macro, profile, options, manualWindow);
        if (!readiness.IsReady) throw new InvalidOperationException(readiness.Reason);
        return readiness.Window;
    }

    private async Task<IntPtr> RecoverFoundWindowAsync(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow, CancellationToken token)
    {
        var readiness = FindTargetReadiness(macro, profile, options, manualWindow);
        if (readiness.IsReady) return readiness.Window;
        return (await RecoverWindowAsync(macro, profile, options, manualWindow, readiness.Reason, HeldInputState.Empty, token)).Window;
    }

    private async Task<IntPtr> PlayOnceAsync(IReadOnlyList<RecordedEvent> events, IntPtr window, MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        foreach (var e in events)
        {
            token.ThrowIfCancellationRequested();
            var beforeWait = await RecoverCurrentWindowIfNeededAsync(window, macro, profile, options, manualWindow, token);
            window = beforeWait.Window;
            started += beforeWait.PauseTicks;

            var due = e.OffsetMicroseconds / options.SpeedMultiplier;
            while (true)
            {
                await WaitUntilAsync(started, due, token);
                var beforeDispatch = await RecoverCurrentWindowIfNeededAsync(window, macro, profile, options, manualWindow, token);
                window = beforeDispatch.Window;
                if (beforeDispatch.PauseTicks == 0) break;
                started += beforeDispatch.PauseTicks;
            }
            Dispatch(window, e, options.Mode);
        }
        return window;
    }

    private async Task<RecoveryResult> RecoverCurrentWindowIfNeededAsync(IntPtr window, MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow, CancellationToken token)
    {
        var readiness = CheckWindowReadiness(window, macro, profile, options, manualWindow);
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
            var readiness = FindTargetReadiness(macro, profile, options, manualWindow);
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

    private TargetReadiness FindTargetReadiness(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        if (manualWindow is { } binding)
        {
            var direct = CheckWindowReadiness(binding.Window.Handle, macro, profile, options, binding);
            if (direct.IsReady) return direct;

            // Launchers can rebuild their top-level HWND while retaining the same
            // process. Rebind only inside the user-selected PID; never select a
            // different Zmbox instance or another application's window.
            var rebound = _targets.TryRebindSelectedZmboxWindow(binding.Window);
            if (rebound is null || rebound.Handle == binding.Window.Handle) return direct;
            return CheckWindowReadiness(
                rebound.Handle,
                macro,
                profile,
                options,
                new ManualWindowBinding(rebound, binding.MatchesSelectedProfile));
        }
        var window = _targets.Find(profile);
        if (window == IntPtr.Zero) return new TargetReadiness(IntPtr.Zero, false, "未找到匹配的目标窗口。");
        return CheckWindowReadiness(window, macro, profile, options, null);
    }

    private TargetReadiness CheckWindowReadiness(IntPtr window, MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        if (manualWindow is { } binding)
        {
            var validation = window == binding.Window.Handle
                ? _targets.ValidateSelectedWindow(binding.Window)
                : _targets.ValidateRecoveredWindow(window, binding.Window.ProcessId);
            if (validation is not null)
            {
                var processIsRebuildingWindow = _targets.IsProcessAlive(binding.Window.ProcessId);
                return new TargetReadiness(IntPtr.Zero, false, validation, processIsRebuildingWindow);
            }
            if (options.Mode == PlaybackMode.ExperimentalTargetWindow && !binding.MatchesSelectedProfile)
                return new TargetReadiness(window, false, "手动选择的窗口不匹配当前配置档，不能使用后台窗口消息。");
        }
        if (!_targets.IsWindow(window)) return new TargetReadiness(IntPtr.Zero, false, "目标窗口句柄已失效。");
        // Focus is checked before geometry. Switching to another foreground app must
        // pause safely, but it is not evidence that the locked Zmbox window moved.
        if (options.Mode == PlaybackMode.ForegroundSystemInput && !_targets.IsForeground(window))
            return new TargetReadiness(window, false, "已切离锁定的造梦盒子窗口，前台输入已暂停。");
        var layout = _targets.CompareLayout(window, macro.DisplayLayout);
        if (!layout.IsMatch) return new TargetReadiness(window, false, layout.Reason, layout.IsTransient);
        if (options.Mode == PlaybackMode.ExperimentalTargetWindow && profile.BackgroundCapability == BackgroundCapability.Unsupported)
            return new TargetReadiness(window, false, "此启动器已标记为不支持后台回放。");
        return new TargetReadiness(window, true, string.Empty);
    }

    private static async Task WaitUntilAsync(long start, double dueMicroseconds, CancellationToken token)
    {
        var dueTicks = (long)(dueMicroseconds * Stopwatch.Frequency / 1_000_000d);
        while (true)
        {
            var remaining = dueTicks - (Stopwatch.GetTimestamp() - start);
            if (remaining <= 0) return;
            var remainingMs = remaining * 1000d / Stopwatch.Frequency;
            if (remainingMs > 3) await Task.Delay(TimeSpan.FromMilliseconds(remainingMs - 2), token);
            else Thread.SpinWait(80);
        }
    }

    private void Dispatch(IntPtr window, RecordedEvent e, PlaybackMode mode)
    {
        if (mode == PlaybackMode.ForegroundSystemInput) SendInput(e); else PostWindowMessage(window, e);
        Track(e);
    }

    private static void SendInput(RecordedEvent e)
    {
        if (e.Kind is InputEventKind.KeyDown or InputEventKind.KeyUp)
        {
            var keyboardInput = new NativeMethods.Input { Type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeyboardInput { Vk = (ushort)e.VirtualKey, Flags = e.Kind == InputEventKind.KeyUp ? NativeMethods.KEYEVENTF_KEYUP : 0 } } };
            if (NativeMethods.SendInput(1, [keyboardInput], Marshal.SizeOf<NativeMethods.Input>()) != 1) throw new InvalidOperationException("Windows 拒绝了键盘输入事件。");
            return;
        }

        var mouse = ToMouseInput(e);
        var mouseInput = new NativeMethods.Input { Type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { Mouse = mouse } };
        if (e.Kind is InputEventKind.MouseDown or InputEventKind.MouseUp or InputEventKind.MouseWheel)
        {
            var position = mouse;
            position.DwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE;
            position.MouseData = 0;
            var inputs = new[] { new NativeMethods.Input { Type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { Mouse = position } }, mouseInput };
            if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) != inputs.Length) throw new InvalidOperationException("Windows 拒绝了鼠标输入事件。");
        }
        else if (NativeMethods.SendInput(1, [mouseInput], Marshal.SizeOf<NativeMethods.Input>()) != 1) throw new InvalidOperationException("Windows 拒绝了鼠标输入事件。");
    }

    private static NativeMethods.MouseInput ToMouseInput(RecordedEvent e)
    {
        uint flags = e.Kind switch
        {
            InputEventKind.MouseMove => NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE,
            InputEventKind.MouseWheel => NativeMethods.MOUSEEVENTF_WHEEL,
            InputEventKind.MouseDown => e.Button switch { MouseButtonKind.Left => NativeMethods.MOUSEEVENTF_LEFTDOWN, MouseButtonKind.Right => NativeMethods.MOUSEEVENTF_RIGHTDOWN, MouseButtonKind.Middle => NativeMethods.MOUSEEVENTF_MIDDLEDOWN, _ => NativeMethods.MOUSEEVENTF_XDOWN },
            InputEventKind.MouseUp => e.Button switch { MouseButtonKind.Left => NativeMethods.MOUSEEVENTF_LEFTUP, MouseButtonKind.Right => NativeMethods.MOUSEEVENTF_RIGHTUP, MouseButtonKind.Middle => NativeMethods.MOUSEEVENTF_MIDDLEUP, _ => NativeMethods.MOUSEEVENTF_XUP },
            _ => 0u
        };
        var width = Math.Max(1, NativeMethods.GetSystemMetrics((int)NativeMethods.SM_CXSCREEN) - 1);
        var height = Math.Max(1, NativeMethods.GetSystemMetrics((int)NativeMethods.SM_CYSCREEN) - 1);
        return new NativeMethods.MouseInput { Dx = e.X * 65535 / width, Dy = e.Y * 65535 / height, MouseData = e.Kind == InputEventKind.MouseWheel ? (uint)e.WheelDelta : e.Button is MouseButtonKind.X1 ? 1u : e.Button is MouseButtonKind.X2 ? 2u : 0u, DwFlags = flags };
    }

    private static void PostWindowMessage(IntPtr window, RecordedEvent e)
    {
        (int message, IntPtr wParam, IntPtr lParam) = e.Kind switch
        {
            InputEventKind.KeyDown => (NativeMethods.WM_KEYDOWN, (IntPtr)e.VirtualKey, NativeMethods.MakeKeyLParam(false)),
            InputEventKind.KeyUp => (NativeMethods.WM_KEYUP, (IntPtr)e.VirtualKey, NativeMethods.MakeKeyLParam(true)),
            InputEventKind.MouseMove => (NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, NativeMethods.MakeMouseLParam(window, e.X, e.Y)),
            InputEventKind.MouseWheel => (NativeMethods.WM_MOUSEWHEEL, (IntPtr)(e.WheelDelta << 16), NativeMethods.MakeMouseLParam(window, e.X, e.Y)),
            InputEventKind.MouseDown => (MouseMessage(e.Button, true), IntPtr.Zero, NativeMethods.MakeMouseLParam(window, e.X, e.Y)),
            InputEventKind.MouseUp => (MouseMessage(e.Button, false), IntPtr.Zero, NativeMethods.MakeMouseLParam(window, e.X, e.Y)),
            _ => throw new InvalidOperationException("未知输入事件。")
        };
        if (!NativeMethods.PostMessage(window, (uint)message, wParam, lParam)) throw new InvalidOperationException("目标窗口拒绝了输入消息。");
    }

    private static int MouseMessage(MouseButtonKind button, bool down) => (button, down) switch
    {
        (MouseButtonKind.Left, true) => NativeMethods.WM_LBUTTONDOWN, (MouseButtonKind.Left, false) => NativeMethods.WM_LBUTTONUP,
        (MouseButtonKind.Right, true) => NativeMethods.WM_RBUTTONDOWN, (MouseButtonKind.Right, false) => NativeMethods.WM_RBUTTONUP,
        (MouseButtonKind.Middle, true) => NativeMethods.WM_MBUTTONDOWN, (MouseButtonKind.Middle, false) => NativeMethods.WM_MBUTTONUP,
        _ => down ? NativeMethods.WM_XBUTTONDOWN : NativeMethods.WM_XBUTTONUP
    };

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
        foreach (var key in held.Keys) Dispatch(window, new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = key }, mode);
        foreach (var button in held.Buttons) Dispatch(window, button with { Kind = InputEventKind.MouseDown }, mode);
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
            if (mode == PlaybackMode.ForegroundSystemInput) SendInputRelease(e);
            else if (_targets.IsWindow(window)) PostWindowMessage(window, e);
        }
        catch
        {
            // 紧急释放是最佳努力操作；不能让释放失败遮蔽原始的停止原因。
        }
    }

    private static void SendInputRelease(RecordedEvent e)
    {
        if (e.Kind == InputEventKind.KeyUp)
        {
            SendInput(e);
            return;
        }

        var mouse = ToMouseInput(e);
        var mouseInput = new NativeMethods.Input { Type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { Mouse = mouse } };
        if (NativeMethods.SendInput(1, [mouseInput], Marshal.SizeOf<NativeMethods.Input>()) != 1)
            throw new InvalidOperationException("Windows 拒绝了鼠标释放事件。");
    }

    private sealed record TargetReadiness(IntPtr Window, bool IsReady, string Reason, bool IsTransient = false);
    private sealed record RecoveryResult(IntPtr Window, long PauseTicks);
    private sealed record HeldInputState(int[] Keys, RecordedEvent[] Buttons)
    {
        public static HeldInputState Empty { get; } = new([], []);
    }
}
