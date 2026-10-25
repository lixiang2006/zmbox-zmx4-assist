using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Services;

/// <summary>
/// Production <see cref="IPlaybackTargetGuard"/>: answers playback readiness from the live
/// window, and rebinds a rebuilt HWND only inside the process the user already locked.
/// </summary>
internal sealed class TargetWindowGuard(TargetWindowService targets) : IPlaybackTargetGuard
{
    public TargetReadiness Find(MacroDefinition macro, ZmboxTargetSettings target, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        if (manualWindow is { } binding)
        {
            var direct = Check(binding.Window.Handle, macro, target, options, binding);
            if (direct.IsReady) return direct;

            // Launchers can rebuild their top-level HWND while retaining the same
            // process. Rebind only inside the user-selected PID; never select a
            // different Zmbox instance or another application's window.
            var rebound = targets.TryRebindSelectedZmboxWindow(binding.Window);
            if (rebound is null || rebound.Handle == binding.Window.Handle) return direct;
            return Check(
                rebound.Handle,
                macro,
                target,
                options,
                new ManualWindowBinding(rebound, binding.MatchesTarget));
        }
        var window = targets.Find();
        if (window == IntPtr.Zero) return new TargetReadiness(IntPtr.Zero, false, "未找到匹配的目标窗口。");
        return Check(window, macro, target, options, null);
    }

    public TargetReadiness Check(IntPtr window, MacroDefinition macro, ZmboxTargetSettings target, PlaybackOptions options, ManualWindowBinding? manualWindow)
    {
        if (manualWindow is { } binding)
        {
            var validation = window == binding.Window.Handle
                ? targets.ValidateSelectedWindow(binding.Window)
                : targets.ValidateRecoveredWindow(window, binding.Window.ProcessId);
            if (validation is not null)
            {
                var processIsRebuildingWindow = targets.IsProcessAlive(binding.Window.ProcessId);
                return new TargetReadiness(IntPtr.Zero, false, validation, processIsRebuildingWindow);
            }
            if (options.Mode == PlaybackMode.ExperimentalTargetWindow && !binding.MatchesTarget)
                return new TargetReadiness(window, false, "手动选择的窗口不匹配当前目标，不能使用后台窗口消息。");
        }
        if (!targets.IsWindow(window)) return new TargetReadiness(IntPtr.Zero, false, "目标窗口句柄已失效。");
        // Focus is checked before geometry. Switching to another foreground app must
        // pause safely, but it is not evidence that the locked Zmbox window moved.
        if (options.Mode == PlaybackMode.ForegroundSystemInput && !targets.IsForeground(window))
            return new TargetReadiness(window, false, "已切离锁定的造梦盒子窗口，前台输入已暂停。");
        var layout = targets.CompareLayout(window, macro.DisplayLayout);
        if (!layout.IsMatch) return new TargetReadiness(window, false, layout.Reason, layout.IsTransient);
        if (options.Mode == PlaybackMode.ExperimentalTargetWindow && target.BackgroundCapability == BackgroundCapability.Unsupported)
            return new TargetReadiness(window, false, "此目标已标记为不支持后台回放。");
        return new TargetReadiness(window, true, string.Empty);
    }
}
