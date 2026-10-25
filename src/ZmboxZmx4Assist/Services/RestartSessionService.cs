using System.Diagnostics;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

/// <summary>
/// Runs one already-validated macro iteration. The session orchestrator owns the
/// outer cancellation token so it never recursively starts a normal playback.
/// </summary>
public interface IPlaybackRoundExecutor
{
    bool IsRunning { get; }
    Task<PlaybackRunResult> PlaySingleIterationAsync(
        MacroDefinition macro,
        ZmboxTargetSettings target,
        PlaybackOptions options,
        ManualWindowBinding manualWindow,
        CancellationToken cancellationToken);
    void EmergencyStop();
}

/// <summary>
/// Deliberately narrow process/window boundary for the restart session. It only
/// acts on the user-locked process and on a PID started by this application.
/// </summary>
public interface IZmboxProcessLifecycle
{
    Task<ProcessCloseResult> RequestNormalCloseAsync(
        ManualWindowBinding lockedWindow,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    Task<ProcessLaunchResult> StartAsync(string executablePath, CancellationToken cancellationToken);
    Task<RestartWindowResult> WaitForWindowAsync(ProcessLaunchResult launchedProcess, TimeSpan timeout, CancellationToken cancellationToken);
    LayoutRestoreResult RestoreLayout(WindowCandidate window, DisplayLayout expectedLayout);
    bool TryActivate(WindowCandidate window);
}

public interface IRestartSessionClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp);
}

public interface IRestartSessionDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed record ProcessCloseResult(bool Succeeded, string Message);
public sealed record ProcessLaunchResult(bool Succeeded, int ProcessId, string Message, DateTime? ProcessStartedAtUtc = null);
public sealed record RestartWindowResult(bool Succeeded, WindowCandidate? Window, string Message, bool CanRetry);
public sealed record LayoutRestoreResult(bool Succeeded, WindowCandidate? Window, string Message);

public sealed class RestartSessionClock : IRestartSessionClock
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => Stopwatch.GetElapsedTime(startTimestamp, endTimestamp);
}

public sealed class RestartSessionDelay : IRestartSessionDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

/// <summary>
/// Coordinates a primary macro and an optional post-restart preparation macro.
/// Input remains delegated to <see cref="IPlaybackRoundExecutor"/>; this class
/// owns only session state and never forces a process to exit.
/// </summary>
public sealed class RestartSessionService
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NewWindowTimeout = TimeSpan.FromSeconds(120);
    private readonly IPlaybackRoundExecutor _roundExecutor;
    private readonly IZmboxProcessLifecycle _processLifecycle;
    private readonly IRestartSessionClock _clock;
    private readonly IRestartSessionDelay _delay;
    private CancellationTokenSource? _running;

    public RestartSessionService(
        IPlaybackRoundExecutor roundExecutor,
        IZmboxProcessLifecycle processLifecycle,
        IRestartSessionClock? clock = null,
        IRestartSessionDelay? delay = null)
    {
        _roundExecutor = roundExecutor;
        _processLifecycle = processLifecycle;
        _clock = clock ?? new RestartSessionClock();
        _delay = delay ?? new RestartSessionDelay();
    }

    public bool IsRunning => _running is not null;
    public event Action<string>? StatusChanged;
    public event Action? StateChanged;
    public event Action<PlaybackStopResult>? PlaybackStopped;
    public event Action<ManualWindowBinding>? WindowBindingChanged;

    public async Task PlayAsync(RestartSessionRequest request)
    {
        if (_running is not null) throw new InvalidOperationException("已有自动重启会话正在运行。");
        if (ValidateRequest(request) is { } validation) throw new InvalidOperationException(validation);

        _running = new CancellationTokenSource();
        StateChanged?.Invoke();
        try
        {
            await RunAsync(request, _running.Token);
            StatusChanged?.Invoke("回放会话完成。");
        }
        catch (SessionStoppedException ex)
        {
            StatusChanged?.Invoke($"回放已停止：{ex.Stop.Reason}");
            PlaybackStopped?.Invoke(ex.Stop);
        }
        catch (OperationCanceledException)
        {
            var stop = new PlaybackStopResult(PlaybackStopKind.UserRequested, "已请求停止。");
            StatusChanged?.Invoke($"回放已停止：{stop.Reason}");
            PlaybackStopped?.Invoke(stop);
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? "自动重启会话发生未知错误。" : ex.Message;
            var stop = new PlaybackStopResult(PlaybackStopKind.RestartFailure, reason);
            StatusChanged?.Invoke($"回放已停止：{reason}");
            PlaybackStopped?.Invoke(stop);
        }
        finally
        {
            // The executor's release path is idempotent and protects every
            // session exit branch, including cancellation while waiting.
            _roundExecutor.EmergencyStop();
            _running.Dispose();
            _running = null;
            StateChanged?.Invoke();
        }
    }

    public void EmergencyStop()
    {
        _running?.Cancel();
        _roundExecutor.EmergencyStop();
    }

    public static string? ValidateRequest(RestartSessionRequest request)
    {
        if (!request.Settings.Enabled) return "自动重启会话尚未启用。";
        if (request.Settings.Validate() is { } settingsError) return settingsError;
        if (request.Settings.PreparationMacroId is null) return "启用自动重启后必须选择准备宏。";
        if (request.PrimaryMacro.Id == request.PreparationMacro.Id) return "准备宏必须与主宏不同。";
        if (request.Settings.PreparationMacroId != request.PreparationMacro.Id) return "准备宏选择已过期，请重新选择。";
        if (MacroValidator.Validate(request.PrimaryMacro) is { } primaryError) return $"主宏不可用：{primaryError}";
        if (MacroValidator.Validate(request.PreparationMacro) is { } preparationError) return $"准备宏不可用：{preparationError}";
        if (request.PrimaryMacro.DisplayLayout is null || request.PreparationMacro.DisplayLayout is null)
            return "主宏和准备宏都必须包含录制时的窗口布局。";
        if (!DisplayLayoutComparer.Compare(request.PrimaryMacro.DisplayLayout, request.PreparationMacro.DisplayLayout).IsMatch)
            return "准备宏的录制布局与主宏不兼容。";
        if (!request.InitialWindow.MatchesTarget ||
            !string.Equals(request.InitialWindow.Window.ProcessName, "造梦盒子", StringComparison.OrdinalIgnoreCase) ||
            !request.InitialWindow.Window.WindowTitle.Contains("造梦盒子", StringComparison.OrdinalIgnoreCase))
            return "自动重启只能用于已锁定的造梦盒子窗口。";
        if (!Path.IsPathFullyQualified(request.ExecutablePath) ||
            !string.Equals(Path.GetExtension(request.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(request.ExecutablePath))
            return "未找到可验证的造梦盒子 EXE 路径，不能安全自动重启。请先锁定一次可读取路径的造梦盒子窗口。";
        if (request.PlaybackOptions.Mode == PlaybackMode.ExperimentalTargetWindow &&
            request.Target.BackgroundCapability != BackgroundCapability.Verified)
            return "后台窗口消息尚未验证，自动重启会话不能启用后台通道。";
        return null;
    }

    private async Task RunAsync(RestartSessionRequest request, CancellationToken token)
    {
        var binding = request.InitialWindow;
        var currentProcessStartedAt = _clock.GetTimestamp();
        var completedPrimaryIterations = 0;

        while (request.PlaybackOptions.InfiniteLoop || completedPrimaryIterations < Math.Max(1, request.PlaybackOptions.RepeatCount))
        {
            token.ThrowIfCancellationRequested();
            completedPrimaryIterations++;
            StatusChanged?.Invoke($"正在回放主宏第 {completedPrimaryIterations} 轮…{RestartCountdownText(request, currentProcessStartedAt)}");
            await ExecuteRoundOrStopAsync(
                request.PrimaryMacro,
                request,
                binding,
                currentProcessStartedAt,
                $"正在回放主宏第 {completedPrimaryIterations} 轮",
                token);

            var hasNextPrimaryIteration = request.PlaybackOptions.InfiniteLoop ||
                completedPrimaryIterations < Math.Max(1, request.PlaybackOptions.RepeatCount);
            if (!hasNextPrimaryIteration) return;

            var restartDue = await WaitBetweenPrimaryIterationsAsync(
                $"主宏第 {completedPrimaryIterations} 轮结束，等待加载完成",
                request.PlaybackOptions.InterIterationDelaySeconds,
                request,
                currentProcessStartedAt,
                token);

            if (!restartDue && request.PlaybackOptions.ShouldCooldownAfter(completedPrimaryIterations, hasNextPrimaryIteration))
            {
                restartDue = await WaitBetweenPrimaryIterationsAsync(
                    $"主宏第 {completedPrimaryIterations} 轮结束，周期冷却",
                    request.PlaybackOptions.CooldownSeconds,
                    request,
                    currentProcessStartedAt,
                    token);
            }

            if (!restartDue && IsRestartDue(request, currentProcessStartedAt)) restartDue = true;
            if (!restartDue) continue;

            StatusChanged?.Invoke($"主宏第 {completedPrimaryIterations} 轮已安全结束，正在正常关闭造梦盒子以重启…");
            var restarted = await RestartTargetAsync(binding, request, token);
            binding = restarted.Binding;
            currentProcessStartedAt = restarted.StartedAt;
            WindowBindingChanged?.Invoke(binding);

            await WaitStartupBufferAsync(request, currentProcessStartedAt, token);
            StatusChanged?.Invoke("启动缓冲完成，正在执行一次准备宏…");
            await ExecuteRoundOrStopAsync(
                request.PreparationMacro,
                request,
                binding,
                currentProcessStartedAt,
                "正在执行准备宏",
                token);
            StatusChanged?.Invoke($"准备宏完成，继续主宏第 {completedPrimaryIterations + 1} 轮…{RestartCountdownText(request, currentProcessStartedAt)}");
        }
    }

    private async Task ExecuteRoundOrStopAsync(
        MacroDefinition macro,
        RestartSessionRequest request,
        ManualWindowBinding binding,
        long currentProcessStartedAt,
        string stage,
        CancellationToken token)
    {
        var run = _roundExecutor.PlaySingleIterationAsync(
            macro,
            request.Target,
            request.PlaybackOptions,
            binding,
            token);
        while (!run.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(1), token));
            if (!run.IsCompleted)
                StatusChanged?.Invoke($"{stage}…{RestartCountdownText(request, currentProcessStartedAt)}");
        }

        var result = await run;
        if (result.Succeeded) return;
        throw new SessionStoppedException(result.StopResult ?? new PlaybackStopResult(PlaybackStopKind.InputFailure, "单轮回放未完成。"));
    }

    private async Task<bool> WaitBetweenPrimaryIterationsAsync(
        string label,
        int seconds,
        RestartSessionRequest request,
        long currentProcessStartedAt,
        CancellationToken token)
    {
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            token.ThrowIfCancellationRequested();
            if (IsRestartDue(request, currentProcessStartedAt)) return true;
            StatusChanged?.Invoke($"{label}：{remaining} 秒…{RestartCountdownText(request, currentProcessStartedAt)}");
            await _delay.DelayAsync(TimeSpan.FromSeconds(1), token);
        }
        return IsRestartDue(request, currentProcessStartedAt);
    }

    private async Task WaitStartupBufferAsync(RestartSessionRequest request, long currentProcessStartedAt, CancellationToken token)
    {
        for (var remaining = request.Settings.StartupBufferSeconds; remaining > 0; remaining--)
        {
            token.ThrowIfCancellationRequested();
            StatusChanged?.Invoke($"造梦盒子已重启，启动缓冲：{remaining} 秒…{RestartCountdownText(request, currentProcessStartedAt)}");
            await _delay.DelayAsync(TimeSpan.FromSeconds(1), token);
        }
    }

    private async Task<RestartedTarget> RestartTargetAsync(
        ManualWindowBinding currentBinding,
        RestartSessionRequest request,
        CancellationToken token)
    {
        var close = await _processLifecycle.RequestNormalCloseAsync(currentBinding, CloseTimeout, token);
        if (!close.Succeeded) throw new InvalidOperationException(close.Message);

        RestartWindowResult? lastWindowResult = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            token.ThrowIfCancellationRequested();
            StatusChanged?.Invoke(attempt == 0 ? "正在启动新的造梦盒子实例…" : "首次启动未出现窗口，正在安全重试一次…");
            var launch = await _processLifecycle.StartAsync(request.ExecutablePath, token);
            if (!launch.Succeeded || launch.ProcessId <= 0)
            {
                if (attempt == 0) continue;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(launch.Message) ? "未获得新启动造梦盒子的有效进程 PID。" : launch.Message);
            }

            var processStartedAt = _clock.GetTimestamp();
            StatusChanged?.Invoke("已启动新实例，正在等待其窗口出现（最长 120 秒）…");
            var window = await _processLifecycle.WaitForWindowAsync(launch, NewWindowTimeout, token);
            if (!window.Succeeded || window.Window is null)
            {
                lastWindowResult = window;
                if (attempt == 0 && window.CanRetry) continue;
                throw new InvalidOperationException(window.Message);
            }

            var restored = _processLifecycle.RestoreLayout(window.Window, request.InitialWindow.Window.Layout);
            if (!restored.Succeeded || restored.Window is null) throw new InvalidOperationException(restored.Message);
            if (request.PlaybackOptions.Mode == PlaybackMode.ForegroundSystemInput && !_processLifecycle.TryActivate(restored.Window))
                throw new InvalidOperationException("新启动的造梦盒子窗口无法置于前台，已停止以避免向错误窗口发送输入。");

            return new RestartedTarget(
                new ManualWindowBinding(restored.Window, true),
                processStartedAt);
        }

        throw new InvalidOperationException(lastWindowResult?.Message ?? "造梦盒子启动后未出现可用窗口。");
    }

    private bool IsRestartDue(RestartSessionRequest request, long currentProcessStartedAt) =>
        _clock.GetElapsedTime(currentProcessStartedAt, _clock.GetTimestamp()) >= TimeSpan.FromMinutes(request.Settings.IntervalMinutes);

    private string RestartCountdownText(RestartSessionRequest request, long currentProcessStartedAt)
    {
        var remaining = TimeSpan.FromMinutes(request.Settings.IntervalMinutes) -
            _clock.GetElapsedTime(currentProcessStartedAt, _clock.GetTimestamp());
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"下次重启约 {seconds / 60:D2}:{seconds % 60:D2}";
    }

    private sealed record RestartedTarget(ManualWindowBinding Binding, long StartedAt);

    private sealed class SessionStoppedException : Exception
    {
        public SessionStoppedException(PlaybackStopResult stop) => Stop = stop;
        public PlaybackStopResult Stop { get; }
    }
}
