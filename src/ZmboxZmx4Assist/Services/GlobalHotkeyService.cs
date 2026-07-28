using System.Windows.Interop;
using System.Windows;
using System.Windows.Input;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Interop;

namespace ZmboxZmx4Assist.Services;

public sealed record HotkeyRegistrationResult(bool RecordRegistered, bool PlayRegistered, bool EmergencyRegistered, string Message, bool Applied = true)
{
    public bool RecordAndPlayRegistered => RecordRegistered && PlayRegistered;
    public bool AllRegistered => RecordAndPlayRegistered && EmergencyRegistered;
}

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly System.Windows.Window _window;
    private HwndSource? _source;
    private HotkeySettings _settings = new();
    public event Action? RecordPressed;
    public event Action? PlayPressed;
    public event Action? EmergencyPressed;

    public GlobalHotkeyService(System.Windows.Window window)
    {
        _window = window;
        _window.SourceInitialized += (_, _) => Register();
    }

    public HotkeyRegistrationResult Configure(HotkeySettings settings)
    {
        Unregister();
        _settings = settings;
        return Register();
    }

    public HotkeyRegistrationResult TryConfigure(HotkeySettings settings)
    {
        var validation = settings.Validate();
        if (validation is not null)
            return new HotkeyRegistrationResult(false, false, false, validation, false);

        var previous = _settings;
        Unregister();
        _settings = settings;
        var candidate = Register();
        if (candidate.AllRegistered)
            return candidate;

        Unregister();
        _settings = previous;
        var restored = Register();
        return candidate with
        {
            Applied = false,
            Message = $"{candidate.Message} 未保存新热键，已恢复旧热键。{restored.Message}"
        };
    }

    private HotkeyRegistrationResult Register()
    {
        _source = PresentationSource.FromVisual(_window) as HwndSource;
        if (_source is null) return new HotkeyRegistrationResult(false, false, false, "窗口尚未初始化，无法注册全局热键。");
        _source.AddHook(WndProc);
        var record = Register(1, _settings.RecordHotkey);
        var play = Register(2, _settings.PlayHotkey);
        var emergency = Register(3, _settings.EmergencyHotkey);
        return new HotkeyRegistrationResult(record, play, emergency, RegistrationMessage(record, play, emergency));
    }
    private bool Register(int id, HotkeyBinding binding) => NativeMethods.RegisterHotKey(_source!.Handle, id, (uint)binding.Modifiers, binding.VirtualKey);
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WM_HOTKEY) return IntPtr.Zero;
        handled = true;
        switch (wParam.ToInt32()) { case 1: RecordPressed?.Invoke(); break; case 2: PlayPressed?.Invoke(); break; case 3: EmergencyPressed?.Invoke(); break; }
        return IntPtr.Zero;
    }
    private void Unregister()
    {
        if (_source is null) return;
        for (var id = 1; id <= 3; id++) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _source.RemoveHook(WndProc); _source = null;
    }

    private string RegistrationMessage(bool record, bool play, bool emergency)
    {
        if (record && play && emergency) return "全局热键已注册。";
        var unavailable = new List<string>();
        if (!record) unavailable.Add(_settings.RecordHotkey.DisplayText);
        if (!play) unavailable.Add(_settings.PlayHotkey.DisplayText);
        if (!emergency) unavailable.Add(_settings.EmergencyHotkey.DisplayText);
        var available = new List<string>();
        if (record) available.Add(_settings.RecordHotkey.DisplayText);
        if (play) available.Add(_settings.PlayHotkey.DisplayText);
        if (emergency) available.Add(_settings.EmergencyHotkey.DisplayText);
        return $"未能注册：{string.Join("、", unavailable)}（可能被其他程序占用）。" +
               (available.Count == 0 ? "" : $"已注册：{string.Join("、", available)}。");
    }

    public void Dispose() => Unregister();
}
