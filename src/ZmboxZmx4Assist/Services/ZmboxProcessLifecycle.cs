using System.Diagnostics;
using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Services;

/// <summary>
/// Windows process lifecycle adapter used exclusively by the optional restart
/// session. It never kills a process or chooses an arbitrary same-named window.
/// </summary>
public sealed class ZmboxProcessLifecycle : IZmboxProcessLifecycle
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private readonly TargetWindowService _targets;

    public ZmboxProcessLifecycle(TargetWindowService targets) => _targets = targets;

    public async Task<ProcessCloseResult> RequestNormalCloseAsync(
        ManualWindowBinding lockedWindow,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!lockedWindow.MatchesTarget || !_targets.IsZmboxWindow(lockedWindow.Window))
            return new ProcessCloseResult(false, "当前锁定的窗口不是可重启的造梦盒子实例。");
        if (!_targets.TryRequestNormalClose(lockedWindow.Window, out var error))
            return new ProcessCloseResult(false, error);

        var started = Stopwatch.GetTimestamp();
        while (_targets.IsProcessInstanceAlive(lockedWindow.Window.ProcessId, lockedWindow.Window.ProcessStartedAtUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= timeout)
                return new ProcessCloseResult(false, "造梦盒子在正常关闭请求后 30 秒内仍未退出；为保护游戏状态，未强制结束进程。请手动处理后再开始新的会话。");
            await Task.Delay(PollInterval, cancellationToken);
        }

        return new ProcessCloseResult(true, "造梦盒子已正常退出。");
    }

    public Task<ProcessLaunchResult> StartAsync(string executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executablePath))
            return Task.FromResult(new ProcessLaunchResult(false, 0, "造梦盒子 EXE 路径不可用，未尝试启动。"));

        try
        {
            var directory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Task.FromResult(new ProcessLaunchResult(false, 0, "造梦盒子 EXE 所在目录不可用，未尝试启动。"));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = directory,
                UseShellExecute = false
            });
            return process is null
                ? Task.FromResult(new ProcessLaunchResult(false, 0, "Windows 未返回新启动的造梦盒子进程。"))
                : Task.FromResult(new ProcessLaunchResult(true, process.Id, "已启动新的造梦盒子进程。", TryGetStartedAtUtc(process)));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.SecurityException)
        {
            return Task.FromResult(new ProcessLaunchResult(false, 0, $"无法启动造梦盒子：{ex.Message}"));
        }
    }

    public async Task<RestartWindowResult> WaitForWindowAsync(ProcessLaunchResult launchedProcess, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = _targets.FindZmboxWindowForProcess(launchedProcess.ProcessId, launchedProcess.ProcessStartedAtUtc);
            if (window is not null)
                return new RestartWindowResult(true, window, "已找到新启动的造梦盒子窗口。", false);

            if (!_targets.IsProcessInstanceAlive(launchedProcess.ProcessId, launchedProcess.ProcessStartedAtUtc))
                return new RestartWindowResult(false, null, "新启动的造梦盒子进程在出现可用窗口前已退出。", true);
            if (Stopwatch.GetElapsedTime(started) >= timeout)
                return new RestartWindowResult(false, null, "新启动的造梦盒子窗口在 120 秒内未出现；进程仍在运行，未再次启动以避免重复实例。", false);

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public LayoutRestoreResult RestoreLayout(WindowCandidate window, DisplayLayout expectedLayout)
    {
        var success = _targets.TryRestoreWindowLayout(window, expectedLayout, out var restored, out var message);
        return new LayoutRestoreResult(success, restored, message);
    }

    public bool TryActivate(WindowCandidate window) => _targets.TryActivate(window);

    private static DateTime? TryGetStartedAtUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
