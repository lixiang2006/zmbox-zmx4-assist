using System.Diagnostics;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Interop;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

public sealed class TargetWindowService
{
    public IntPtr Find(TargetProfile profile) => profile.Id == ZmboxTargetSettings.TargetProfileId ? FindZmbox() : NativeMethods.FindWindow(profile);

    public IntPtr FindZmbox()
    {
        return ListVisibleTopLevelWindows().FirstOrDefault(IsZmboxWindow)?.Handle ?? IntPtr.Zero;
    }

    public IReadOnlyList<WindowCandidate> ListVisibleTopLevelWindows() => NativeMethods.ListVisibleTopLevelWindows();

    public WindowCandidate? TryRebindSelectedZmboxWindow(WindowCandidate selected) =>
        ListVisibleTopLevelWindows().FirstOrDefault(candidate =>
            candidate.ProcessId == selected.ProcessId &&
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

    public bool MatchesProfile(WindowCandidate window, TargetProfile profile)
    {
        if (profile.Id == ZmboxTargetSettings.TargetProfileId) return IsZmboxWindow(window);
        if (!window.WindowTitle.Contains(profile.WindowTitleContains, StringComparison.OrdinalIgnoreCase)) return false;
        var expectedPath = Path.GetFullPath(profile.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(window.ExecutablePath))
            return string.Equals(Path.GetFullPath(window.ExecutablePath), expectedPath, StringComparison.OrdinalIgnoreCase);
        return string.Equals(window.ProcessName, Path.GetFileNameWithoutExtension(expectedPath), StringComparison.OrdinalIgnoreCase);
    }

    public string? ValidateSelectedWindow(WindowCandidate window)
    {
        if (!IsWindow(window.Handle)) return "你选择的窗口已关闭或句柄已失效，请重新选择。";
        if (!NativeMethods.IsSameProcess(window.Handle, window.ProcessId)) return "你选择的窗口已被另一个进程复用，请重新选择。";
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

    public TargetPreflightResult Inspect(TargetProfile profile, MacroDefinition? macro, PlaybackMode mode)
    {
        var window = Find(profile);
        if (window == IntPtr.Zero)
            return new TargetPreflightResult(false, string.Empty, false, false, profile.BackgroundCapability != BackgroundCapability.Unsupported, "未找到匹配的目标窗口。请确认启动器、路径与窗口标题。");

        var layout = macro?.DisplayLayout is null
            ? new LayoutComparison(true, "未选择宏，无需比较录制布局。")
            : CompareLayout(window, macro.DisplayLayout);
        var layoutMatches = layout.IsMatch;
        var backgroundAllowed = profile.BackgroundCapability != BackgroundCapability.Unsupported;
        var foreground = IsForeground(window);
        var title = NativeMethods.GetWindowTitle(window);
        var message = !layoutMatches
            ? layout.Reason
            : mode == PlaybackMode.ForegroundSystemInput && !foreground
                ? "窗口已找到；前台回放前请先将目标窗口置于前台。"
                : mode == PlaybackMode.ExperimentalTargetWindow && !backgroundAllowed
                    ? "该配置档已标记为不支持后台窗口消息，仅可前台回放。"
                    : "目标窗口与当前回放条件正常。";
        return new TargetPreflightResult(true, title, foreground, layoutMatches, backgroundAllowed, message);
    }

    public TargetPreflightResult Inspect(ManualWindowBinding binding, TargetProfile profile, MacroDefinition? macro, PlaybackMode mode)
    {
        var window = binding.Window;
        var validation = ValidateSelectedWindow(window);
        if (validation is not null)
            return new TargetPreflightResult(false, window.WindowTitle, false, false, false, validation);

        var layout = macro?.DisplayLayout is null
            ? new LayoutComparison(true, "未选择宏，无需比较录制布局。")
            : CompareLayout(window.Handle, macro.DisplayLayout);
        var foreground = IsForeground(window.Handle);
        var backgroundAllowed = binding.MatchesSelectedProfile && profile.BackgroundCapability != BackgroundCapability.Unsupported;
        var message = !layout.IsMatch
            ? layout.Reason
            : !binding.MatchesSelectedProfile
                ? "手动选择的窗口不匹配当前配置档：仅可使用前台系统输入。"
                : mode == PlaybackMode.ForegroundSystemInput && !foreground
                    ? "已手动选择窗口；开始回放时会尝试将其置于前台。"
                    : mode == PlaybackMode.ExperimentalTargetWindow && !backgroundAllowed
                        ? "该配置档不允许后台窗口消息。"
                        : "已手动绑定此窗口；本次回放不会自动改选同名窗口。";
        return new TargetPreflightResult(true, window.WindowTitle, foreground, layout.IsMatch, backgroundAllowed, message);
    }
}
