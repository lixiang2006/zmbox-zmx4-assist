using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using WpfColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ZmboxZmx4Assist;

public partial class MainWindow : System.Windows.Window
{
    private readonly MacroLibraryService _library = new();
    private readonly TargetWindowService _targets = new();
    private readonly WindowHighlightService _highlighter;
    private readonly GlobalInputRecorder _recorder;
    private readonly PlaybackService _player;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly RecordingIndicatorWindow _recordingIndicator = new();
    private readonly ObservableCollection<MacroListItem> _macros = [];
    private readonly ObservableCollection<TargetProfile> _profiles = [];
    private ZmboxTargetSettings _targetSettings = new();
    private HotkeySettings _hotkeySettings = new();
    private HotkeyBinding _recordHotkeyDraft = HotkeyBinding.RecordDefault;
    private HotkeyBinding _playHotkeyDraft = HotkeyBinding.PlayDefault;
    private HotkeyBinding _emergencyHotkeyDraft = HotkeyBinding.EmergencyDefault;
    private WpfTextBox? _activeHotkeyCapture;
    private HotkeyBinding? _hotkeyCaptureOriginal;
    private Action<string, string>? _notify;
    private Action<string, string>? _errorNotify;
    private ManualWindowBinding? _manualWindow;
    private bool _captureInProgress;

    public MainWindow()
    {
        InitializeComponent();
        _highlighter = new WindowHighlightService(_targets);
        _recorder = new GlobalInputRecorder(_targets);
        _player = new PlaybackService(_targets);
        _hotkeys = new GlobalHotkeyService(this);
        _recorder.RecordingStopped += OnRecordingStopped;
        _player.StatusChanged += SetStatus;
        _player.PlaybackStopped += OnPlaybackStopped;
        _player.StateChanged += RefreshControlState;
        _hotkeys.RecordPressed += () => { if (!IsCapturingHotkey) RecordButton_Click(this, new RoutedEventArgs()); };
        _hotkeys.PlayPressed += async () => { if (!IsCapturingHotkey) await PlayAsync(); };
        _hotkeys.EmergencyPressed += () => { if (!IsCapturingHotkey) StopPlayback(); };
        SpeedSlider.ValueChanged += (_, _) => SpeedText.Text = $"{SpeedSlider.Value:F2}x";
        Loaded += (_, _) => LoadLibrary();
        Closed += (_, _) => { _highlighter.Dispose(); _recordingIndicator.Close(); _hotkeys.Dispose(); _recorder.Dispose(); _player.EmergencyStop(); };
    }

    public void SetNotifier(Action<string, string> notifier, Action<string, string> errorNotifier)
    {
        _notify = notifier;
        _errorNotify = errorNotifier;
    }

    private void LoadLibrary()
    {
        _profiles.Clear();
        _targetSettings = _library.LoadZmboxTarget();
        _profiles.Add(_targetSettings.ToCompatibilityProfile());
        var load = _library.LoadMacrosWithIssues();
        RebuildMacroItems(load.Macros);
        MacroIssueText.Visibility = load.Issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        MacroIssueText.Text = load.Issues.Count == 0 ? string.Empty : $"已跳过 {load.Issues.Count} 个损坏宏文件：{string.Join("、", load.Issues.Take(3).Select(x => x.FileName))}。原文件未移动或删除。";
        _hotkeySettings = _library.LoadHotkeys();
        SyncHotkeyDrafts(_hotkeySettings);
        var hotkeyResult = _hotkeys.Configure(_hotkeySettings);
        ProfilesCombo.ItemsSource = _profiles;
        MacrosList.ItemsSource = _macros;
        ProfilesCombo.SelectedIndex = _profiles.Count > 0 ? 0 : -1;
        if (_macros.Count > 0) MacrosList.SelectedIndex = 0;
        if (!hotkeyResult.AllRegistered) SetStatus(hotkeyResult.Message);
        RefreshAll();
    }

    // This is a single-target application. Do not derive the active target from the
    // hidden legacy ComboBox: collection notifications could otherwise clear a lock.
    private TargetProfile SelectedProfile => _targetSettings.ToCompatibilityProfile();
    private MacroListItem? SelectedMacroItem => MacrosList.SelectedItem as MacroListItem;
    private MacroDefinition? SelectedMacro => SelectedMacroItem?.Macro;

    private void RebuildMacroItems(IEnumerable<MacroDefinition> macros)
    {
        _macros.Clear();
        foreach (var macro in macros) _macros.Add(new MacroListItem(macro, ProfileNameFor(macro.TargetProfileId)));
    }

    private string ProfileNameFor(Guid profileId) => ZmboxTargetSettings.DisplayName;

    private void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profile = SelectedProfile;
        ProfileNameText.Text = profile.Name;
        ExecutablePathText.Text = profile.ExecutablePath;
        WindowTitleText.Text = profile.WindowTitleContains;
        RefreshAll();
    }

    private void MacrosList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A locked Zmbox instance belongs to the playback session, not to one
        // macro. Switching macros must not force the user through capture again.
        SelectedMacroText.Text = SelectedMacroItem is { } item ? $"已选：{item.DetailLine}" : "新录制将保存为下列名称";
        RefreshAll();
    }

    private void PlaybackModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshAll();
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        var name = ProfileNameText.Text.Trim();
        var executable = ExecutablePathText.Text.Trim();
        var title = WindowTitleText.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(title))
        {
            SetStatus("配置档名称、EXE 路径和窗口标题均不能为空。");
            return;
        }
        ReplaceProfile(profile with { Name = name, ExecutablePath = executable, WindowTitleContains = title });
        SetStatus("目标配置档已安全保存。");
    }

    private void CloneProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;
        var copy = profile with { Id = Guid.NewGuid(), Name = profile.Name + " 副本", BackgroundCapability = BackgroundCapability.Unknown };
        _profiles.Add(copy);
        _library.SaveProfiles(_profiles);
        ProfilesCombo.SelectedItem = copy;
        SetStatus("已复制配置档；请先用无破坏性宏验证后台能力。");
    }

    private void ReplaceProfile(TargetProfile replacement)
    {
        var index = _profiles.IndexOf(SelectedProfile!);
        _profiles[index] = replacement;
        ProfilesCombo.SelectedItem = replacement;
        foreach (var item in _macros) item.Replace(item.Macro, ProfileNameFor(item.Macro.TargetProfileId));
        _library.SaveProfiles(_profiles);
        RefreshAll();
    }

    private void RecordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording) { FinishRecording(); return; }
        var profile = SelectedProfile;
        if (!_recorder.Start(profile, _hotkeySettings)) { ShowRecordingResult("录制只能在目标窗口位于前台时启动。", false); return; }
        _recordingIndicator.ShowRecording(_hotkeySettings.RecordHotkey.DisplayText);
        _notify?.Invoke("Zmbox ZMX4 Assist", $"已开始录制；按 {_hotkeySettings.RecordHotkey.DisplayText} 结束并保存。");
        SetStatus("正在录制；切换离开目标窗口会自动丢弃本次录制。");
        RefreshControlState();
    }

    private void FinishRecording()
    {
        var profile = SelectedProfile;
        var window = _targets.Find(profile);
        if (_recorder.WasAborted || window == IntPtr.Zero) { _recorder.Abort("录制已丢弃。"); RefreshControlState(); return; }
        var requestedName = MacroNameText.Text.Trim();
        var name = MacroLibraryService.ValidateMacroName(requestedName) is null ? requestedName : "未命名宏";
        var macro = _recorder.Stop(name, profile.Id, _targets.GetLayout(window));
        try
        {
            _library.SaveMacro(macro);
            var item = new MacroListItem(macro, ProfileNameFor(macro.TargetProfileId));
            _macros.Add(item);
            MacrosList.SelectedItem = item;
            ShowRecordingResult($"已保存“{macro.Name}” · {macro.Events.Count} 个事件");
        }
        catch (Exception ex)
        {
            ShowRecordingResult($"保存宏失败：{ex.Message}", false);
        }
        RefreshControlState();
    }

    private void DeleteMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMacroItem is not { } item) return;
        var answer = WpfMessageBox.Show($"确定删除“{item.Name}”？\n\n{item.DetailLine}\n此操作会删除本机宏文件。", "删除宏", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _library.DeleteMacro(item.Macro);
        _macros.Remove(item);
        SetStatus($"已删除“{item.Name}”。");
        RefreshAll();
    }

    private void MacroName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not MacroListItem item || _recorder.IsRecording || _player.IsRunning) return;
        MacrosList.SelectedItem = item;
        item.BeginRename();
        Dispatcher.BeginInvoke(() => FocusRenameEditor(item));
        e.Handled = true;
    }

    private void FocusRenameEditor(MacroListItem item)
    {
        if (MacrosList.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject container) return;
        var editor = FindVisualChild<WpfTextBox>(container);
        editor?.Focus();
        editor?.SelectAll();
    }

    private void MacroRenameText_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MacroListItem item) return;
        if (e.Key == Key.Enter) { CommitRename(item); e.Handled = true; }
        if (e.Key == Key.Escape) { item.CancelRename(); e.Handled = true; }
    }

    private void MacroRenameText_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MacroListItem item && item.IsRenaming) CommitRename(item);
    }

    private void CommitRename(MacroListItem item)
    {
        try
        {
            var renamed = _library.RenameMacro(item.Macro, item.DraftName);
            item.Replace(renamed, ProfileNameFor(renamed.TargetProfileId));
            SetStatus($"已将宏改名为“{renamed.Name}”。");
        }
        catch (Exception ex)
        {
            item.CancelRename();
            SetStatus(ex.Message);
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await PlayAsync();

    private async Task PlayAsync()
    {
        if (SelectedMacro is not { } macro) { SetStatus("请选择要回放的宏。"); return; }
        var profile = SelectedProfile;
        if (!int.TryParse(RepeatText.Text, out var repeats) || repeats < 1) { SetStatus("循环次数必须是正整数。"); return; }
        if (!int.TryParse(InterIterationDelayText.Text, out var delaySeconds) || delaySeconds is < 0 or > 120) { SetStatus("每轮等待必须是 0 到 120 秒之间的整数。"); return; }
        if (!int.TryParse(CooldownEveryIterationsText.Text, out var cooldownEvery) || cooldownEvery is < 1 or > 1000) { SetStatus("周期冷却轮数必须是 1 到 1000 之间的整数。"); return; }
        if (!int.TryParse(CooldownSecondsText.Text, out var cooldownSeconds) || cooldownSeconds is < 0 or > 900) { SetStatus("周期冷却等待必须是 0 到 900 秒之间的整数。"); return; }
        if (macro.TargetProfileId != profile.Id)
        {
            var original = ProfileNameFor(macro.TargetProfileId);
            var confirmation = WpfMessageBox.Show($"此宏录制于“{original}”，当前将回放到“{profile.Name}”。\n\n坐标和窗口行为可能不同。是否继续？", "跨配置档回放", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes) { SetStatus("已取消跨配置档回放。"); return; }
        }
        var mode = CurrentPlaybackMode;
        try
        {
            if (_manualWindow is { } existing && _targets.ValidateSelectedWindow(existing.Window) is not null)
            {
                // A stale HWND must never be reused. Valid locks, however, are a
                // user choice and deliberately survive normal playback stops.
                ClearManualWindow(false);
            }
            if (_manualWindow is null && !await CapturePlaybackWindowAsync()) return;
            profile = SelectedProfile;
            var selectedWindow = _manualWindow!;
            if (mode == PlaybackMode.ExperimentalTargetWindow && !selectedWindow.MatchesSelectedProfile)
            {
                SetStatus("手动选择的窗口不匹配当前配置档，不能使用后台窗口消息。");
                return;
            }
            if (mode == PlaybackMode.ForegroundSystemInput && !_targets.TryActivate(selectedWindow.Window))
            {
                SetStatus("无法将所选窗口置于前台；请手动点击该窗口后重试。");
                return;
            }
            await _player.PlayAsync(macro, profile, new PlaybackOptions(repeats, InfiniteCheck.IsChecked == true, SpeedSlider.Value, mode, delaySeconds, cooldownEvery, cooldownSeconds), selectedWindow);
        }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally
        {
            _highlighter.Stop();
            RefreshAll();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void StopPlayback() { _highlighter.Stop(); _player.EmergencyStop(); SetStatus("已请求紧急停止并释放输入。"); }
    private void BackgroundPassedButton_Click(object sender, RoutedEventArgs e) => MarkBackground(BackgroundCapability.Verified);
    private void BackgroundFailedButton_Click(object sender, RoutedEventArgs e) => MarkBackground(BackgroundCapability.Unsupported);

    private void MarkBackground(BackgroundCapability result)
    {
        var profile = SelectedProfile;
        _targetSettings = _targetSettings with { BackgroundCapability = result };
        _library.SaveZmboxTarget(_targetSettings);
        ReplaceProfile(profile with { BackgroundCapability = result });
        SetStatus(result == BackgroundCapability.Verified ? "造梦盒子已启用实验性后台回放。" : "造梦盒子已标记为不支持后台回放。");
    }

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

    private void CheckTargetButton_Click(object sender, RoutedEventArgs e) => RefreshPreflight();
    private async void SelectWindowButton_Click(object sender, RoutedEventArgs e) => await CapturePlaybackWindowAsync();

    private async Task<bool> CapturePlaybackWindowAsync()
    {
        if (_captureInProgress)
        {
            SetStatus("正在锁定造梦盒子窗口，请勿重复启动。 ");
            return false;
        }
        _captureInProgress = true;
        try
        {
            var profile = SelectedProfile;

            var capture = new ForegroundWindowCaptureDialog(_targets) { Owner = this };
            if (capture.ShowDialog() != true || capture.CapturedWindow is not { } selected)
            {
                SetStatus("未锁定回放窗口。请重新开始倒计时，并在结束前点击游戏窗口。");
                return false;
            }

            var rememberedProfile = RememberCapturedWindow(profile, selected);
            var matchesProfile = _targets.IsZmboxWindow(selected);
            _manualWindow = new ManualWindowBinding(selected, matchesProfile);
            SelectedWindowText.Text =
                $"本次已锁定前台窗口\n标题：{selected.WindowTitle}\n进程：{selected.ProcessName} · PID {selected.ProcessId}\n" +
                $"位置与尺寸：({selected.Layout.WindowX}, {selected.Layout.WindowY}) · {selected.Layout.WindowWidth} × {selected.Layout.WindowHeight}\n开始后仅绑定此窗口。";
            if (!_targets.TryActivate(selected))
            {
                ClearManualWindow(false);
                SetStatus("无法将锁定窗口置于前台；请重新开始倒计时。 ");
                return false;
            }
            SetStatus("已锁定回放窗口，正在显示 1.5 秒物理像素边框…");
            var highlight = await _highlighter.ShowAsync(selected);
            if (!highlight.Succeeded)
            {
                ClearManualWindow(false);
                SetStatus(highlight.Message);
                return false;
            }
            SetStatus(matchesProfile
                ? "已锁定造梦盒子前台窗口，并已自动更新目标信息。"
                : "已锁定前台窗口；路径未读取时保留原路径，本次仅可前台回放。");
            RefreshPreflight();
            return true;
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private TargetProfile RememberCapturedWindow(TargetProfile profile, WindowCandidate selected)
    {
        var executable = string.IsNullOrWhiteSpace(selected.ExecutablePath) ? profile.ExecutablePath : selected.ExecutablePath;
        var executableChanged = !string.Equals(profile.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase);
        var changed = executableChanged || !string.Equals(profile.WindowTitleContains, selected.WindowTitle, StringComparison.Ordinal);
        if (!changed) return profile;

        var updated = profile with
        {
            ExecutablePath = executable,
            WindowTitleContains = selected.WindowTitle,
            BackgroundCapability = executableChanged ? BackgroundCapability.Unknown : profile.BackgroundCapability
        };
        // Keep the locked HWND intact. Updating a selected ComboBox item used to
        // raise SelectionChanged and accidentally discard the manual binding.
        ProfileNameText.Text = updated.Name;
        ExecutablePathText.Text = updated.ExecutablePath;
        WindowTitleText.Text = updated.WindowTitleContains;
        _targetSettings = new ZmboxTargetSettings
        {
            ExecutablePath = updated.ExecutablePath,
            WindowTitleContains = updated.WindowTitleContains,
            BackgroundCapability = updated.BackgroundCapability
        };
        _library.SaveZmboxTarget(_targetSettings);
        return updated;
    }

    private void ClearManualWindow(bool refresh = true)
    {
        _manualWindow = null;
        if (SelectedWindowText is not null)
            SelectedWindowText.Text = "开始回放会给出 3 秒倒计时；请在结束前点击要回放的游戏窗口。";
        if (refresh && IsLoaded) RefreshPreflight();
    }

    private void OpenMacrosFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = _library.MacrosDirectory, UseShellExecute = true });
    }

    private bool IsCapturingHotkey => _activeHotkeyCapture is not null;

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

    private void OnRecordingStopped(string reason)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnRecordingStopped(reason)); return; }
        if (_recorder.WasAborted) ShowRecordingResult(reason, false);
        else SetStatus(reason);
        RefreshControlState();
    }

    private void ShowRecordingResult(string message, bool success = true)
    {
        _recordingIndicator.ShowResult(message, success);
        _notify?.Invoke("Zmbox ZMX4 Assist", message);
        SetStatus(message);
    }

    private void OnPlaybackStopped(PlaybackStopResult result)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnPlaybackStopped(result)); return; }
        if (!result.IsUnexpected) return;
        var message = $"回放已停止：{result.Reason}";
        _recordingIndicator.ShowResult(message, false, TimeSpan.FromSeconds(5));
        _errorNotify?.Invoke("Zmbox ZMX4 Assist 回放停止", result.Reason);
        SetStatus(message);
    }

    private PlaybackMode CurrentPlaybackMode => PlaybackModeCombo.SelectedIndex == 1 ? PlaybackMode.ExperimentalTargetWindow : PlaybackMode.ForegroundSystemInput;

    private void RefreshAll()
    {
        RefreshMacroSummary();
        RefreshBackgroundState();
        RefreshPreflight();
        RefreshControlState();
    }

    private void RefreshMacroSummary()
    {
        MacroCountText.Text = _macros.Count == 0 ? "尚无宏；输入名称后开始录制" : $"共 {_macros.Count} 个本地宏";
        SelectedMacroText.Text = SelectedMacroItem is { } item ? $"已选：{item.DetailLine}" : "新录制将保存为下列名称";
    }

    private void RefreshBackgroundState()
    {
        if (SelectedProfile is not { } profile) return;
        var unsupported = profile.BackgroundCapability == BackgroundCapability.Unsupported;
        BackgroundModeItem.IsEnabled = !unsupported;
        if (unsupported && PlaybackModeCombo.SelectedIndex == 1) PlaybackModeCombo.SelectedIndex = 0;
        BackgroundStatusText.Text = unsupported
            ? "该配置档已验证不接收后台窗口消息：仅支持前台系统输入。"
            : profile.BackgroundCapability == BackgroundCapability.Verified
                ? "此配置档已由你验证为支持实验性后台窗口消息。"
                : "后台兼容性尚未验证；请只用无破坏性宏测试。";
    }

    private void RefreshPreflight()
    {
        var profile = SelectedProfile;
        var result = _manualWindow is { } binding
            ? _targets.Inspect(binding, profile, SelectedMacro, CurrentPlaybackMode)
            : _targets.Inspect(profile, SelectedMacro, CurrentPlaybackMode);
        PreflightTitleText.Text = _manualWindow is not null
            ? (result.TargetFound ? "已锁定回放窗口" : "锁定的窗口不可用")
            : result.TargetFound ? "目标窗口已发现" : "未发现目标窗口";
        PreflightDetailText.Text = result.Message;
        TargetWindowText.Text = _manualWindow is { } locked
            ? $"窗口：{locked.Window.WindowTitle}\n进程：{locked.Window.ProcessName} · PID {locked.Window.ProcessId}\n路径：{(string.IsNullOrWhiteSpace(locked.Window.ExecutablePath) ? "未读取到" : locked.Window.ExecutablePath)}"
            : result.TargetFound ? $"窗口：{result.WindowTitle}" : $"路径：{profile.ExecutablePath}";
        LayoutText.Text = SelectedMacro?.DisplayLayout is null ? "布局：选择宏后检查录制布局。" : $"布局：{(result.LayoutMatches ? "与录制时一致" : "与录制时不一致")}";
        ChannelText.Text = $"通道：前台 {(result.IsForeground ? "已就绪" : "需置前")} · 后台 {(result.BackgroundAllowed ? "可测试" : "已禁用")}";
    }

    private void RefreshControlState()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshControlState); return; }
        if (!IsLoaded) return;
        var busy = _recorder.IsRecording || _player.IsRunning;
        RecordButton.IsEnabled = !_player.IsRunning;
        RecordButton.Content = _recorder.IsRecording ? $"结束并保存 ({_hotkeySettings.RecordHotkey.DisplayText})" : $"开始录制 ({_hotkeySettings.RecordHotkey.DisplayText})";
        PlayButton.IsEnabled = !busy && SelectedMacro is not null && SelectedProfile is not null;
        PlayButton.Content = $"开始回放 ({_hotkeySettings.PlayHotkey.DisplayText})";
        StopButton.Content = $"紧急停止 ({_hotkeySettings.EmergencyHotkey.DisplayText})";
        DeleteMacroButton.IsEnabled = !busy && SelectedMacro is not null;
        CloneProfileButton.IsEnabled = !busy;
        SaveProfileButton.IsEnabled = !busy;
        SaveHotkeysButton.IsEnabled = !busy;
        RecordHotkeyText.IsEnabled = !busy;
        PlayHotkeyText.IsEnabled = !busy;
        EmergencyHotkeyText.IsEnabled = !busy;
        CheckTargetButton.IsEnabled = !busy;
        SelectWindowButton.IsEnabled = !busy && SelectedProfile is not null;
        BackgroundPassedButton.IsEnabled = !busy;
        BackgroundFailedButton.IsEnabled = !busy;
    }

    private void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SetStatus(text)); return; }
        StatusText.Text = text;
        StatusPill.Background = text.Contains("失败") || text.Contains("错误") || text.Contains("找不到") || text.Contains("不支持") || text.Contains("已停止")
            ? new SolidColorBrush(WpfColor.FromRgb(92, 45, 38))
            : text.Contains("等待") || text.Contains("冷却") || text.Contains("录制") || text.Contains("回放")
                ? new SolidColorBrush(WpfColor.FromRgb(18, 61, 57))
                : new SolidColorBrush(WpfColor.FromRgb(35, 53, 68));
        RefreshControlState();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
