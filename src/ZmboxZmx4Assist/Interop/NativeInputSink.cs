using System.Runtime.InteropServices;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Interop;

/// <summary>
/// Production <see cref="IInputSink"/>: the only place playback turns recorded events into
/// SendInput calls or posted window messages.
/// </summary>
internal sealed class NativeInputSink : IInputSink
{
    public void Dispatch(IntPtr window, RecordedEvent e, PlaybackMode mode, VirtualScreenBounds bounds)
    {
        if (mode == PlaybackMode.ForegroundSystemInput) SendInputEvent(e, bounds);
        else PostWindowMessage(window, e);
    }

    public void Release(IntPtr window, RecordedEvent e, PlaybackMode mode)
    {
        if (mode == PlaybackMode.ForegroundSystemInput)
        {
            SendInputRelease(e);
            return;
        }
        if (NativeMethods.IsWindow(window)) PostWindowMessage(window, e);
    }

    public bool TryProbeBackgroundChannel(IntPtr window, out string reason)
    {
        reason = string.Empty;
        if (!NativeMethods.IsWindow(window))
        {
            reason = "目标窗口已关闭，无法投递后台输入。";
            return false;
        }

        if (NativeMethods.PostMessage(window, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero)) return true;
        reason = DescribeRejection(window, Marshal.GetLastWin32Error());
        return false;
    }

    private static string DescribeRejection(IntPtr window, int errorCode)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var target = processId == 0 ? IntegrityBand.Unknown : ProcessIntegrity.OfProcess((int)processId);
        return ProcessIntegrity.DescribeWindowMessageFailure(errorCode, ProcessIntegrity.OfCurrentProcess(), target, ProgramDirectory());
    }

    private static string? ProgramDirectory() => Path.GetDirectoryName(Environment.ProcessPath);

    private static void SendInputEvent(RecordedEvent e, VirtualScreenBounds bounds)
    {
        if (e.Kind is InputEventKind.KeyDown or InputEventKind.KeyUp)
        {
            // Extended keys (arrows, Home/End, right Ctrl/Alt, ...) only work when the injected
            // event carries KEYEVENTF_EXTENDEDKEY; without it SendInput delivers the numpad key.
            var flags = (e.Kind == InputEventKind.KeyUp ? NativeMethods.KEYEVENTF_KEYUP : 0u)
                | (NativeMethods.IsExtendedKey(e.VirtualKey) ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0u);
            var keyboardInput = new NativeMethods.Input { Type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeyboardInput { Vk = (ushort)e.VirtualKey, Flags = flags } } };
            if (NativeMethods.SendInput(1, [keyboardInput], NativeMethods.InputSize) != 1) throw new InvalidOperationException("Windows 拒绝了键盘输入事件。");
            return;
        }

        var mouse = ToMouseInput(e, bounds);
        var mouseInput = new NativeMethods.Input { Type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { Mouse = mouse } };
        if (e.Kind is InputEventKind.MouseDown or InputEventKind.MouseUp or InputEventKind.MouseWheel)
        {
            var position = mouse;
            position.DwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE;
            position.MouseData = 0;
            var inputs = new[] { new NativeMethods.Input { Type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { Mouse = position } }, mouseInput };
            if (NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.InputSize) != inputs.Length) throw new InvalidOperationException("Windows 拒绝了鼠标输入事件。");
        }
        else if (NativeMethods.SendInput(1, [mouseInput], NativeMethods.InputSize) != 1) throw new InvalidOperationException("Windows 拒绝了鼠标输入事件。");
    }

    private static NativeMethods.MouseInput ToMouseInput(RecordedEvent e, VirtualScreenBounds bounds)
    {
        uint flags = e.Kind switch
        {
            InputEventKind.MouseMove => NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE,
            InputEventKind.MouseWheel => NativeMethods.MOUSEEVENTF_WHEEL,
            InputEventKind.MouseDown => e.Button switch { MouseButtonKind.Left => NativeMethods.MOUSEEVENTF_LEFTDOWN, MouseButtonKind.Right => NativeMethods.MOUSEEVENTF_RIGHTDOWN, MouseButtonKind.Middle => NativeMethods.MOUSEEVENTF_MIDDLEDOWN, _ => NativeMethods.MOUSEEVENTF_XDOWN },
            InputEventKind.MouseUp => e.Button switch { MouseButtonKind.Left => NativeMethods.MOUSEEVENTF_LEFTUP, MouseButtonKind.Right => NativeMethods.MOUSEEVENTF_RIGHTUP, MouseButtonKind.Middle => NativeMethods.MOUSEEVENTF_MIDDLEUP, _ => NativeMethods.MOUSEEVENTF_XUP },
            _ => 0u
        };
        var (dx, dy) = MouseCoordinateTranslator.ToAbsolute(e.X, e.Y, bounds);
        return new NativeMethods.MouseInput { Dx = dx, Dy = dy, MouseData = e.Kind == InputEventKind.MouseWheel ? (uint)e.WheelDelta : e.Button is MouseButtonKind.X1 ? 1u : e.Button is MouseButtonKind.X2 ? 2u : 0u, DwFlags = flags };
    }

    private static void PostWindowMessage(IntPtr window, RecordedEvent e)
    {
        (int message, IntPtr wParam, IntPtr lParam) = e.Kind switch
        {
            InputEventKind.KeyDown => (NativeMethods.WM_KEYDOWN, (IntPtr)e.VirtualKey, NativeMethods.MakeKeyLParam(false, e.VirtualKey)),
            InputEventKind.KeyUp => (NativeMethods.WM_KEYUP, (IntPtr)e.VirtualKey, NativeMethods.MakeKeyLParam(true, e.VirtualKey)),
            InputEventKind.MouseMove => (NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, NativeMethods.MakeMouseLParam(window, e.X, e.Y)),
            // The wheel delta lives in the high word; the low word carries modifier state (0 here).
            // lParam is in screen coordinates for this message, not client coordinates.
            InputEventKind.MouseWheel => (NativeMethods.WM_MOUSEWHEEL, (IntPtr)((uint)(ushort)e.WheelDelta << 16), NativeMethods.MakeWheelLParam(e.X, e.Y)),
            InputEventKind.MouseDown => (MouseMessage(e.Button, true), IntPtr.Zero, NativeMethods.MakeMouseLParam(window, e.X, e.Y)),
            InputEventKind.MouseUp => (MouseMessage(e.Button, false), IntPtr.Zero, NativeMethods.MakeMouseLParam(window, e.X, e.Y)),
            _ => throw new InvalidOperationException("未知输入事件。")
        };
        if (!NativeMethods.PostMessage(window, (uint)message, wParam, lParam))
            throw new InvalidOperationException(DescribeRejection(window, Marshal.GetLastWin32Error()));
    }

    private static int MouseMessage(MouseButtonKind button, bool down) => (button, down) switch
    {
        (MouseButtonKind.Left, true) => NativeMethods.WM_LBUTTONDOWN,
        (MouseButtonKind.Left, false) => NativeMethods.WM_LBUTTONUP,
        (MouseButtonKind.Right, true) => NativeMethods.WM_RBUTTONDOWN,
        (MouseButtonKind.Right, false) => NativeMethods.WM_RBUTTONUP,
        (MouseButtonKind.Middle, true) => NativeMethods.WM_MBUTTONDOWN,
        (MouseButtonKind.Middle, false) => NativeMethods.WM_MBUTTONUP,
        _ => down ? NativeMethods.WM_XBUTTONDOWN : NativeMethods.WM_XBUTTONUP
    };

    private static void SendInputRelease(RecordedEvent e)
    {
        if (e.Kind == InputEventKind.KeyUp)
        {
            SendInputEvent(e, default);
            return;
        }

        var mouse = ToMouseInput(e, default);
        var mouseInput = new NativeMethods.Input { Type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { Mouse = mouse } };
        if (NativeMethods.SendInput(1, [mouseInput], NativeMethods.InputSize) != 1)
            throw new InvalidOperationException("Windows 拒绝了鼠标释放事件。");
    }
}
