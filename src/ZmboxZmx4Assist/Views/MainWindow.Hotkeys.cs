using System.Windows;
using System.Windows.Input;
using ZmboxZmx4Assist.Domain;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ZmboxZmx4Assist.Views;

// Global hotkeys: the capture boxes, draft bindings and saving/validation.
public partial class MainWindow
{
    private bool IsCapturingHotkey => _activeHotkeyCapture is not null;

    private void SaveHotkeysButton_Click(object sender, RoutedEventArgs e)
    {
        var candidate = new HotkeySettings
        {
            RecordHotkey = _recordHotkeyDraft,
            PlayHotkey = _playHotkeyDraft,
            EmergencyHotkey = _emergencyHotkeyDraft
        };
        var validation = candidate.Validate();
        if (validation is not null)
        {
            SetStatus(validation);
            return;
        }
        var result = _hotkeys.TryConfigure(candidate);
        if (!result.Applied)
        {
            SyncHotkeyDrafts(_hotkeySettings);
            SetStatus(result.Message);
            return;
        }
        _hotkeySettings = candidate;
        _library.SaveHotkeys(_hotkeySettings);
        SyncHotkeyDrafts(_hotkeySettings);
        SetStatus("全局热键已保存；录制时会自动排除这些控制按键及其修饰键。");
    }

    private void HotkeyCapture_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _activeHotkeyCapture = sender as WpfTextBox;
        _hotkeyCaptureOriginal = _activeHotkeyCapture is null ? null : GetHotkeyDraft(_activeHotkeyCapture);
        SetStatus("请按下新的组合热键；按 Esc 取消。保存前不会替换当前热键。");
    }

    private void HotkeyCapture_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(_activeHotkeyCapture, sender))
        {
            _activeHotkeyCapture = null;
            _hotkeyCaptureOriginal = null;
        }
    }

    private void HotkeyCapture_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not WpfTextBox box) return;
        if (e.Key == Key.Escape)
        {
            if (_hotkeyCaptureOriginal is { } original) SetHotkeyDraft(box, original);
            Keyboard.ClearFocus();
            SetStatus("已取消热键输入，尚未保存任何更改。");
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) { e.Handled = true; return; }
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var binding = new HotkeyBinding(virtualKey, CurrentHotkeyModifiers());
        if (!binding.IsValid)
        {
            SetStatus("请选择一个非修饰键作为热键主键。");
            e.Handled = true;
            return;
        }
        SetHotkeyDraft(box, binding);
        SetStatus($"已选择 {binding.DisplayText}；点击“保存热键”后生效。");
        e.Handled = true;
    }

    private void SyncHotkeyDrafts(HotkeySettings settings)
    {
        _recordHotkeyDraft = settings.RecordHotkey;
        _playHotkeyDraft = settings.PlayHotkey;
        _emergencyHotkeyDraft = settings.EmergencyHotkey;
        RecordHotkeyText.Text = _recordHotkeyDraft.DisplayText;
        PlayHotkeyText.Text = _playHotkeyDraft.DisplayText;
        EmergencyHotkeyText.Text = _emergencyHotkeyDraft.DisplayText;
    }

    private void SetHotkeyDraft(WpfTextBox source, HotkeyBinding binding)
    {
        if (ReferenceEquals(source, RecordHotkeyText)) _recordHotkeyDraft = binding;
        else if (ReferenceEquals(source, PlayHotkeyText)) _playHotkeyDraft = binding;
        else if (ReferenceEquals(source, EmergencyHotkeyText)) _emergencyHotkeyDraft = binding;
        source.Text = binding.DisplayText;
    }

    private HotkeyBinding GetHotkeyDraft(WpfTextBox source) =>
        ReferenceEquals(source, RecordHotkeyText) ? _recordHotkeyDraft :
        ReferenceEquals(source, PlayHotkeyText) ? _playHotkeyDraft :
        _emergencyHotkeyDraft;

    private static HotkeyModifiers CurrentHotkeyModifiers()
    {
        var modifiers = Keyboard.Modifiers;
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= HotkeyModifiers.Windows;
        return result;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
