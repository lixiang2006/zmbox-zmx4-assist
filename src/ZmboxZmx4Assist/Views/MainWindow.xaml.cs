using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using ZmboxZmx4Assist.ViewModels;
using WpfColor = System.Windows.Media.Color;

namespace ZmboxZmx4Assist.Views;

/// <summary>
/// Core of the main window: service composition, shared state and the cross-cutting status /
/// control-refresh helpers. Feature behaviour lives in the MainWindow.*.cs partial files
/// (recording, playback, restart sessions, target window, macros, hotkeys), so this file only
/// grows when the window gains a new service or a new cross-cutting concern.
/// </summary>
public partial class MainWindow : System.Windows.Window
{
    private readonly MacroLibraryService _library = new();
    private readonly TargetWindowService _targets = new();
    private readonly WindowHighlightService _highlighter;
    private readonly GlobalInputRecorder _recorder;
    private readonly PlaybackService _player;
    private readonly ZmboxProcessLifecycle _processLifecycle;
    private readonly RestartSessionService _restartSession;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly RecordingIndicatorWindow _recordingIndicator = new();
    private readonly ObservableCollection<MacroListItem> _macros = [];
    private readonly ObservableCollection<TargetProfile> _profiles = [];
    private ZmboxTargetSettings _targetSettings = new();
    private HotkeySettings _hotkeySettings = new();
    private HotkeyBinding _recordHotkeyDraft = HotkeyBinding.RecordDefault;
    private HotkeyBinding _playHotkeyDraft = HotkeyBinding.PlayDefault;
    private HotkeyBinding _emergencyHotkeyDraft = HotkeyBinding.EmergencyDefault;
    private System.Windows.Controls.TextBox? _activeHotkeyCapture;
    private HotkeyBinding? _hotkeyCaptureOriginal;
    private Action<string, string>? _notify;
    private Action<string, string>? _errorNotify;
    private ManualWindowBinding? _manualWindow;
    private bool _captureInProgress;
    private bool _syncingRestartSessionSettings;

    public MainWindow()
    {
        InitializeComponent();
        _highlighter = new WindowHighlightService(_targets);
        _recorder = new GlobalInputRecorder(_targets);
        _player = new PlaybackService(_targets);
        _processLifecycle = new ZmboxProcessLifecycle(_targets);
        _restartSession = new RestartSessionService(_player, _processLifecycle);
        _hotkeys = new GlobalHotkeyService(this);
        _recorder.RecordingStopped += OnRecordingStopped;
        _player.StatusChanged += SetStatus;
        _player.PlaybackStopped += OnPlaybackStopped;
        _player.StateChanged += RefreshControlState;
        _restartSession.StatusChanged += SetStatus;
        _restartSession.PlaybackStopped += OnPlaybackStopped;
        _restartSession.StateChanged += RefreshControlState;
        _restartSession.WindowBindingChanged += OnRestartSessionWindowBindingChanged;
        _hotkeys.RecordPressed += () => { if (!IsCapturingHotkey) RecordButton_Click(this, new RoutedEventArgs()); };
        _hotkeys.PlayPressed += async () => { if (!IsCapturingHotkey) await PlayAsync(); };
        _hotkeys.EmergencyPressed += () => { if (!IsCapturingHotkey) StopPlayback(); };
        SpeedSlider.ValueChanged += (_, _) => SpeedText.Text = $"{SpeedSlider.Value:F2}x";
        Loaded += (_, _) => LoadLibrary();
        Closed += (_, _) =>
        {
            _highlighter.Dispose();
            _restartSession.EmergencyStop();
            _player.EmergencyStop();
            _recordingIndicator.Close();
            _hotkeys.Dispose();
            _recorder.Dispose();
        };
    }

    public void SetNotifier(Action<string, string> notifier, Action<string, string> errorNotifier)
    {
        _notify = notifier;
        _errorNotify = errorNotifier;
    }

    // This is a single-target application. Do not derive the active target from the
    // hidden legacy ComboBox: collection notifications could otherwise clear a lock.
    private TargetProfile SelectedProfile => _targetSettings.ToCompatibilityProfile();
    private MacroListItem? SelectedMacroItem => MacrosList.SelectedItem as MacroListItem;
    private MacroDefinition? SelectedMacro => SelectedMacroItem?.Macro;

    private void RefreshAll()
    {
        RefreshMacroSummary();
        RefreshBackgroundState();
        RefreshRestartSessionUi();
        RefreshPreflight();
        RefreshControlState();
    }

    private void RefreshControlState()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshControlState); return; }
        if (!IsLoaded) return;
        var busy = _recorder.IsRecording || _player.IsRunning || _restartSession.IsRunning;
        RecordButton.IsEnabled = !_player.IsRunning && !_restartSession.IsRunning;
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
        RestartSessionEnabledCheck.IsEnabled = !busy;
        RestartIntervalMinutesText.IsEnabled = !busy && _targetSettings.RestartSession.Enabled;
        RestartStartupBufferSecondsText.IsEnabled = !busy && _targetSettings.RestartSession.Enabled;
        RestartPreparationMacroCombo.IsEnabled = !busy && _targetSettings.RestartSession.Enabled;
    }

    private void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SetStatus(text)); return; }
        StatusText.Text = text;
        StatusPill.Background = text.Contains("失败") || text.Contains("错误") || text.Contains("找不到") || text.Contains("不支持") || text.Contains("已停止")
            ? new SolidColorBrush(WpfColor.FromRgb(92, 45, 38))
            : text.Contains("等待") || text.Contains("冷却") || text.Contains("录制") || text.Contains("回放") || text.Contains("重启") || text.Contains("准备") || text.Contains("启动")
                ? new SolidColorBrush(WpfColor.FromRgb(18, 61, 57))
                : new SolidColorBrush(WpfColor.FromRgb(35, 53, 68));
        RefreshControlState();
    }
}
