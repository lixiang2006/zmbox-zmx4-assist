using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Utilities;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Interop;

internal static class NativeMethods
{
    internal const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    internal const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    internal const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    internal const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    internal const int WM_HOTKEY = 0x0312;
    internal const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040,
        MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_ABSOLUTE = 0x8000;
    internal const uint SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private const long WS_POPUP = unchecked((long)0x80000000), SS_BLACKRECT = 0x00000004L;
    private const long WS_EX_TRANSPARENT = 0x00000020L, WS_EX_TOOLWINDOW = 0x00000080L, WS_EX_NOACTIVATE = 0x08000000L, WS_EX_TOPMOST = 0x00000008L;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERZORDER = 0x0200;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct KbdLlHookStruct { public uint VkCode, ScanCode, Flags, Time; public IntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MsLlHookStruct { public Point Pt; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int Dx, Dy; public uint MouseData, DwFlags, Time; public IntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool ScreenToClient(IntPtr hWnd, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int valueSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(long exStyle, string className, string? windowName, long style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    internal static DisplayLayout GetDisplayLayout(IntPtr window)
    {
        if (TryGetDisplayLayout(window, out var layout)) return layout;
        return new DisplayLayout(GetSystemMetrics((int)SM_CXSCREEN), GetSystemMetrics((int)SM_CYSCREEN), (int)GetDpiForSystem(), -65_535, -65_535, 0, 0);
    }

    internal static bool TryGetDisplayLayout(IntPtr window, out DisplayLayout layout)
    {
        layout = null!;
        if (!IsWindow(window) || IsIconic(window) || !GetWindowRect(window, out var rect)) return false;

        var candidate = new DisplayLayout(
            GetSystemMetrics((int)SM_CXSCREEN),
            GetSystemMetrics((int)SM_CYSCREEN),
            (int)GetDpiForSystem(),
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top);
        if (!DisplayLayoutComparer.HasUsableWindowBounds(candidate)) return false;

        layout = candidate;
        return true;
    }

    internal static IntPtr FindWindow(TargetProfile profile)
    {
        var executable = Path.GetFullPath(profile.ExecutablePath);
        var executableName = Path.GetFileNameWithoutExtension(executable);
        IntPtr found = IntPtr.Zero;
        EnumWindows((candidate, _) =>
        {
            if (!IsWindowVisible(candidate)) return true;
            GetWindowThreadProcessId(candidate, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var title = new System.Text.StringBuilder(512);
                GetWindowText(candidate, title, title.Capacity);
                if (!title.ToString().Contains(profile.WindowTitleContains, StringComparison.OrdinalIgnoreCase)) return true;

                // Some runtimes (including the updated AIR launcher) deny MainModule access
                // to normal-user processes. Keep exact-path matching whenever it is available;
                // only then fall back to the configured executable name plus the title match above.
                string? actualExecutable = null;
                try { actualExecutable = process.MainModule?.FileName; }
                catch (Win32Exception) { }
                var executableMatches = !string.IsNullOrWhiteSpace(actualExecutable)
                    ? string.Equals(Path.GetFullPath(actualExecutable), executable, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(process.ProcessName, executableName, StringComparison.OrdinalIgnoreCase);
                if (!executableMatches) return true;

                found = candidate;
                return false;
            }
            catch { return true; }
        }, IntPtr.Zero);
        return found;
    }

    internal static IReadOnlyList<WindowCandidate> ListVisibleTopLevelWindows()
    {
        var windows = new List<WindowCandidate>();
        var ownProcessId = Environment.ProcessId;
        EnumWindows((candidate, _) =>
        {
            if (!IsWindowVisible(candidate)) return true;
            var title = GetWindowTitle(candidate).Trim();
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (!TryGetDisplayLayout(candidate, out var layout)) return true;

            GetWindowThreadProcessId(candidate, out var processId);
            if (processId == 0 || processId == ownProcessId) return true;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                string executablePath = string.Empty;
                try { executablePath = process.MainModule?.FileName ?? string.Empty; }
                catch (Win32Exception) { }

                windows.Add(new WindowCandidate(
                    candidate,
                    (int)processId,
                    process.ProcessName,
                    executablePath,
                    title,
                    layout));
            }
            catch
            {
                // A process can end while the list is being enumerated. Skip that stale item.
            }
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static WindowCandidate? TryGetWindowCandidate(IntPtr window)
    {
        if (!IsWindow(window) || !IsWindowVisible(window)) return null;
        var title = GetWindowTitle(window).Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (!TryGetDisplayLayout(window, out var layout)) return null;

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            string executablePath = string.Empty;
            try { executablePath = process.MainModule?.FileName ?? string.Empty; }
            catch (Win32Exception) { }

            return new WindowCandidate(
                window,
                (int)processId,
                process.ProcessName,
                executablePath,
                title,
                layout);
        }
        catch
        {
            // The foreground process can end while it is being captured.
            return null;
        }
    }

    internal static bool IsSameProcess(IntPtr window, int expectedProcessId)
    {
        if (!IsWindow(window)) return false;
        GetWindowThreadProcessId(window, out var processId);
        return processId == expectedProcessId;
    }

    internal static IntPtr MakeMouseLParam(IntPtr window, int screenX, int screenY)
    {
        var point = new Point { X = screenX, Y = screenY };
        ScreenToClient(window, ref point);
        return (IntPtr)((point.Y << 16) | (point.X & 0xffff));
    }

    internal static bool TryGetVisualBounds(IntPtr window, out PhysicalRect bounds)
    {
        bounds = default;
        if (!IsWindow(window)) return false;
        var size = Marshal.SizeOf<Rect>();
        if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, size) >= 0 && frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            bounds = new PhysicalRect(frame.Left, frame.Top, frame.Right, frame.Bottom);
            return true;
        }
        if (!GetWindowRect(window, out frame) || frame.Right <= frame.Left || frame.Bottom <= frame.Top) return false;
        bounds = new PhysicalRect(frame.Left, frame.Top, frame.Right, frame.Bottom);
        return true;
    }

    internal static IntPtr CreateHighlightBar() => CreateWindowEx(
        WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        "STATIC", null, WS_POPUP | SS_BLACKRECT, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    internal static void PositionHighlightBar(IntPtr bar, PhysicalRect bounds)
    {
        if (bar == IntPtr.Zero || !bounds.IsValid) return;
        SetWindowPos(bar, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
    }

    internal static string GetWindowTitle(IntPtr window)
    {
        var title = new System.Text.StringBuilder(512);
        GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }

    internal static IntPtr MakeKeyLParam(bool keyUp) => (IntPtr)(1 | (keyUp ? unchecked((int)0xC0000000) : 0));
}
