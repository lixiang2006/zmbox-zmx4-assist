using System.Diagnostics;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Interop;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

public sealed class TargetWindowService
{
    public IntPtr Find() => FindZmbox();

    public IntPtr FindZmbox()
    {
        return ListVisibleTopLevelWindows().FirstOrDefault(IsZmboxWindow)?.Handle ?? IntPtr.Zero;
    }

    public IReadOnlyList<WindowCandidate> ListVisibleTopLevelWindows() => NativeMethods.ListVisibleTopLevelWindows();

    public WindowCandidate? TryRebindSelectedZmboxWindow(WindowCandidate selected) =>
        ListVisibleTopLevelWindows().FirstOrDefault(candidate =>
            candidate.ProcessId == selected.ProcessId &&
            IsSameProcessInstance(candidate, selected) &&
            IsZmboxWindow(candidate));

    /// <summary>Finds only a window owned by the exact PID this tool launched.</summary>
    public WindowCandidate? FindZmboxWindowForProcess(int processId, DateTime? expectedStartedAtUtc = null) =>
        processId <= 0
            ? null
            : ListVisibleTopLevelWindows().FirstOrDefault(candidate =>
                candidate.ProcessId == processId &&
                (expectedStartedAtUtc is null || candidate.ProcessStartedAtUtc == expectedStartedAtUtc) &&
                IsZmboxWindow(candidate));

    public WindowCandidate? CaptureForegroundWindow() => NativeMethods.TryGetWindowCandidate(NativeMethods.GetForegroundWindow());

    public WindowCandidate? CaptureForegroundZmboxWindow()
    {
        var window = CaptureForegroundWindow();
        return window is not null && IsZmboxWindow(window) ? window : null;
    }

    public bool IsZmboxWindow(WindowCandidate window) =>
        string.Equals(window.ProcessName, "造梦盒子", StringComparison.OrdinalIgnoreCase) &&
        window.WindowTitle.Contains("造梦盒子", StringComparison.OrdinalIgnoreCase);

    public bool IsWindow(IntPtr window) => window != IntPtr.Zero && NativeMethods.IsWindow(window);

    public bool IsProcessAlive(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool IsProcessInstanceAlive(int processId, DateTime? expectedStartedAtUtc)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return false;
            return expectedStartedAtUtc is null || process.StartTime.ToUniversalTime() == expectedStartedAtUtc.Value;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Normal-user process metadata can be inaccessible. The PID check
            // remains the only available identity signal in that narrow case.
            return true;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    public bool IsForeground(IntPtr window) => window != IntPtr.Zero && NativeMethods.GetForegroundWindow() == window;

    public LayoutComparison CompareLayout(IntPtr window, DisplayLayout? expected)
    {
        if (expected is null) return new LayoutComparison(false, "宏缺少录制时的显示布局。");
        if (!IsWindow(window)) return new LayoutComparison(false, "目标窗口句柄已失效。");
        if (!NativeMethods.TryGetDisplayLayout(window, out var actual))
            return new LayoutComparison(false, "目标窗口暂时没有可用布局，可能正在切换、最小化或重建。", IsTransient: true);
        return DisplayLayoutComparer.Compare(expected, actual);
    }

    public bool LayoutMatches(IntPtr window, DisplayLayout? expected) => CompareLayout(window, expected).IsMatch;

    public DisplayLayout GetLayout(IntPtr window) => NativeMethods.GetDisplayLayout(window);

    public string? ValidateSelectedWindow(WindowCandidate window)
    {
        if (!IsWindow(window.Handle)) return "你选择的窗口已关闭或句柄已失效，请重新选择。";
        if (!NativeMethods.IsSameProcess(window.Handle, window.ProcessId)) return "你选择的窗口已被另一个进程复用，请重新选择。";
        if (!HasExpectedProcessStartTime(window)) return "你选择的窗口 PID 已被新的进程复用，请重新选择。";
        if (!NativeMethods.IsWindowVisible(window.Handle)) return "你选择的窗口当前不可见，请重新选择。";
        return null;
    }

    public string? ValidateRecoveredWindow(IntPtr window, int lockedProcessId)
    {
        if (!IsWindow(window)) return "已锁定的造梦盒子窗口句柄已失效。";
        if (!NativeMethods.IsSameProcess(window, lockedProcessId)) return "恢复出的窗口不属于已锁定的造梦盒子进程。";
        if (!NativeMethods.IsWindowVisible(window)) return "已锁定的造梦盒子窗口当前暂不可见。";
        return null;
    }

    public bool TryActivate(WindowCandidate window) => ValidateSelectedWindow(window) is null && NativeMethods.SetForegroundWindow(window.Handle);

    public bool TryRequestNormalClose(WindowCandidate window, out string message)
    {
        if (!IsZmboxWindow(window))
        {
            message = "当前锁定窗口不是造梦盒子，未发送关闭请求。";
            return false;
        }
        if (ValidateSelectedWindow(window) is { } validation)
        {
            message = validation;
            return false;
        }
        if (!NativeMethods.PostMessage(window.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
        {
            message = "Windows 未能向已锁定的造梦盒子窗口发送正常关闭请求。";
            return false;
        }

        message = "已发送正常关闭请求。";
        return true;
    }

    public bool TryRestoreWindowLayout(
        WindowCandidate window,
        DisplayLayout expected,
        out WindowCandidate? restored,
        out string message)
    {
        restored = null;
        if (ValidateSelectedWindow(window) is { } validation)
        {
            message = validation;
            return false;
        }
        if (window.Layout.Width != expected.Width || window.Layout.Height != expected.Height)
        {
            message = $"新启动实例的主屏分辨率与锁定时不一致（{window.Layout.Width}×{window.Layout.Height}），未移动窗口。";
            return false;
        }
        if (window.Layout.Dpi != expected.Dpi)
        {
            message = $"新启动实例的主屏 DPI 与锁定时不一致（{window.Layout.Dpi}），未移动窗口。";
            return false;
        }
        if (!NativeMethods.TrySetWindowBounds(window.Handle, expected))
        {
            message = "Windows 未能恢复新启动造梦盒子窗口的位置与尺寸。";
            return false;
        }

        var refreshed = NativeMethods.TryGetWindowCandidate(window.Handle);
        if (refreshed is null || !IsSameProcessInstance(refreshed, window))
        {
            message = "恢复窗口布局后，目标窗口已失效或进程实例发生变化。";
            return false;
        }
        var comparison = DisplayLayoutComparer.Compare(expected, refreshed.Layout);
        if (!comparison.IsMatch)
        {
            message = $"已尝试恢复窗口布局，但复检失败：{comparison.Reason}";
            return false;
        }

        restored = refreshed;
        message = "已恢复并复检新启动窗口的物理位置与尺寸。";
        return true;
    }

    public TargetPreflightResult Inspect(ZmboxTargetSettings target, MacroDefinition? macro, PlaybackMode mode)
    {
        var window = Find();
        if (window == IntPtr.Zero)
            return new TargetPreflightResult(false, string.Empty, false, false, target.BackgroundCapability != BackgroundCapability.Unsupported, "未找到匹配的目标窗口。请确认启动器、路径与窗口标题。");

        var layout = macro?.DisplayLayout is null
            ? new LayoutComparison(true, "未选择宏，无需比较录制布局。")
            : CompareLayout(window, macro.DisplayLayout);
        var layoutMatches = layout.IsMatch;
        var backgroundAllowed = target.BackgroundCapability != BackgroundCapability.Unsupported;
        var foreground = IsForeground(window);
        var title = NativeMethods.GetWindowTitle(window);
        var message = !layoutMatches
            ? layout.Reason
            : mode == PlaybackMode.ForegroundSystemInput && !foreground
                ? "窗口已找到；前台回放前请先将目标窗口置于前台。"
                : mode == PlaybackMode.ExperimentalTargetWindow && !backgroundAllowed
                    ? "该目标已标记为不支持后台窗口消息，仅可前台回放。"
                    : "目标窗口与当前回放条件正常。";
        return new TargetPreflightResult(true, title, foreground, layoutMatches, backgroundAllowed, message);
    }

    public TargetPreflightResult Inspect(ManualWindowBinding binding, ZmboxTargetSettings target, MacroDefinition? macro, PlaybackMode mode)
    {
        var window = binding.Window;
        var validation = ValidateSelectedWindow(window);
        if (validation is not null)
            return new TargetPreflightResult(false, window.WindowTitle, false, false, false, validation);

        var layout = macro?.DisplayLayout is null
            ? new LayoutComparison(true, "未选择宏，无需比较录制布局。")
            : CompareLayout(window.Handle, macro.DisplayLayout);
        var foreground = IsForeground(window.Handle);
        var backgroundAllowed = binding.MatchesTarget && target.BackgroundCapability != BackgroundCapability.Unsupported;
        var message = !layout.IsMatch
            ? layout.Reason
            : !binding.MatchesTarget
                ? "手动选择的窗口不匹配当前目标：仅可使用前台系统输入。"
                : mode == PlaybackMode.ForegroundSystemInput && !foreground
                    ? "已手动选择窗口；开始回放时会尝试将其置于前台。"
                    : mode == PlaybackMode.ExperimentalTargetWindow && !backgroundAllowed
                        ? "该目标不允许后台窗口消息。"
                        : "已手动绑定此窗口；本次回放不会自动改选同名窗口。";
        return new TargetPreflightResult(true, window.WindowTitle, foreground, layout.IsMatch, backgroundAllowed, message);
    }

    private static bool IsSameProcessInstance(WindowCandidate current, WindowCandidate expected) =>
        current.ProcessId == expected.ProcessId &&
        (current.ProcessStartedAtUtc is null || expected.ProcessStartedAtUtc is null ||
            current.ProcessStartedAtUtc.Value == expected.ProcessStartedAtUtc.Value);

    private static bool HasExpectedProcessStartTime(WindowCandidate window)
    {
        if (window.ProcessStartedAtUtc is null) return true;
        try
        {
            using var process = Process.GetProcessById(window.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime() == window.ProcessStartedAtUtc.Value;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Existing playback treats an unreadable normal-user process as a
            // usable lock. Preserve that compatibility while still detecting a
            // demonstrably different process instance when Windows exposes it.
            return true;
        }
    }
}
