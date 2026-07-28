using System.Diagnostics;
using System.Runtime.InteropServices;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Interop;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

public sealed class GlobalInputRecorder : IDisposable
{
    private readonly TargetWindowService _targets;
    private readonly List<RecordedEvent> _events = [];
    private NativeMethods.HookProc? _keyboardCallback, _mouseCallback;
    private IntPtr _keyboardHook, _mouseHook, _target;
    private long _startedAt;
    private HotkeySettings _hotkeys = new();

    public bool IsRecording { get; private set; }
    public bool WasAborted { get; private set; }
    public event Action<string>? RecordingStopped;

    public GlobalInputRecorder(TargetWindowService targets) => _targets = targets;

    public bool Start(TargetProfile profile, HotkeySettings hotkeys)
    {
        _target = _targets.Find(profile);
        if (_target == IntPtr.Zero || !_targets.IsForeground(_target)) return false;
        _events.Clear();
        WasAborted = false;
        _hotkeys = hotkeys;
        _startedAt = Stopwatch.GetTimestamp();
        _keyboardCallback = KeyboardHook; _mouseCallback = MouseHook;
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardCallback, module, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseCallback, module, 0);
        IsRecording = _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
        if (!IsRecording) StopInternal("无法安装键鼠监听。");
        return IsRecording;
    }

    public MacroDefinition Stop(string name, Guid profileId, DisplayLayout layout)
    {
        var normalized = MouseGestureProcessor.Normalize(_events);
        StopInternal("录制完成。");
        return new MacroDefinition { Name = name, TargetProfileId = profileId, DisplayLayout = layout, Events = normalized };
    }

    public void Abort(string reason) { WasAborted = true; StopInternal(reason); }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsRecording)
        {
            if (!_targets.IsForeground(_target)) Abort("已切换离开目标窗口，未保存本次录制。");
            else
            {
                var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                if (_hotkeys.IsControlKey(data.VkCode)) return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
                var message = wParam.ToInt32();
                if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                    _events.Add(new RecordedEvent { OffsetMicroseconds = ElapsedMicroseconds(), Kind = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN ? InputEventKind.KeyDown : InputEventKind.KeyUp, VirtualKey = (int)data.VkCode });
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsRecording)
        {
            if (!_targets.IsForeground(_target)) Abort("已切换离开目标窗口，未保存本次录制。");
            else
            {
                var data = Marshal.PtrToStructure<NativeMethods.MsLlHookStruct>(lParam);
                var e = MouseEvent(wParam.ToInt32(), data);
                if (e is not null) _events.Add(e with { OffsetMicroseconds = ElapsedMicroseconds() });
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private static RecordedEvent? MouseEvent(int message, NativeMethods.MsLlHookStruct data)
    {
        var translated = message switch
        {
            NativeMethods.WM_MOUSEMOVE => new RecordedEvent { Kind = InputEventKind.MouseMove, X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_MOUSEWHEEL => new RecordedEvent { Kind = InputEventKind.MouseWheel, X = data.Pt.X, Y = data.Pt.Y, WheelDelta = (short)(data.MouseData >> 16) },
            NativeMethods.WM_LBUTTONDOWN => new RecordedEvent { Kind = InputEventKind.MouseDown, Button = MouseButtonKind.Left, X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_LBUTTONUP => new RecordedEvent { Kind = InputEventKind.MouseUp, Button = MouseButtonKind.Left, X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_RBUTTONDOWN => new RecordedEvent { Kind = InputEventKind.MouseDown, Button = MouseButtonKind.Right, X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_RBUTTONUP => new RecordedEvent { Kind = InputEventKind.MouseUp, Button = MouseButtonKind.Right, X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_MBUTTONDOWN => new RecordedEvent { Kind = InputEventKind.MouseDown, Button = MouseButtonKind.Middle, X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_MBUTTONUP => new RecordedEvent { Kind = InputEventKind.MouseUp, Button = MouseButtonKind.Middle, X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_XBUTTONDOWN => new RecordedEvent { Kind = InputEventKind.MouseDown, Button = LowLevelMouseTranslator.XButtonFromMouseData(data.MouseData), X = data.Pt.X, Y = data.Pt.Y },
            NativeMethods.WM_XBUTTONUP => new RecordedEvent { Kind = InputEventKind.MouseUp, Button = LowLevelMouseTranslator.XButtonFromMouseData(data.MouseData), X = data.Pt.X, Y = data.Pt.Y },
            _ => null
        };
        return translated is { Button: MouseButtonKind.None, Kind: InputEventKind.MouseDown or InputEventKind.MouseUp } ? null : translated;
    }

    private long ElapsedMicroseconds() => (Stopwatch.GetTimestamp() - _startedAt) * 1_000_000L / Stopwatch.Frequency;
    private void StopInternal(string reason)
    {
        if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
        if (IsRecording) RecordingStopped?.Invoke(reason);
        IsRecording = false;
    }
    public void Dispose() => StopInternal("录制已停止。");
}
