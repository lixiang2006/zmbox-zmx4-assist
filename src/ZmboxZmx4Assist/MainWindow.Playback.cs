using System.Windows;
using System.Windows.Controls;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace ZmboxZmx4Assist;

// Playback session entry points: pre-flight validation, window lock acquisition, the
// playback/restart-session dispatch and the emergency-stop path.
public partial class MainWindow
{
    private void PlaybackModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshAll();
    }

    private PlaybackMode CurrentPlaybackMode => PlaybackModeCombo.SelectedIndex == 1 ? PlaybackMode.ExperimentalTargetWindow : PlaybackMode.ForegroundSystemInput;

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await PlayAsync();

    private async Task PlayAsync()
    {
        if (_recorder.IsRecording)
        {
            SetStatus("正在录制；请先结束录制再开始回放。");
            return;
        }
        if (_restartSession.IsRunning || _player.IsRunning)
        {
            SetStatus("已有回放会话正在运行。按紧急停止后再开始新的回放。");
            return;
        }
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
            var options = new PlaybackOptions(repeats, InfiniteCheck.IsChecked == true, SpeedSlider.Value, mode, delaySeconds, cooldownEvery, cooldownSeconds);
            if (_targetSettings.RestartSession.Enabled)
            {
                if (!PersistRestartSessionSettingsIfValid()) return;
                if (CreateRestartSessionRequest(macro, profile, options, selectedWindow) is not { } request) return;
                await _restartSession.PlayAsync(request);
            }
            else
            {
                await _player.PlayAsync(macro, profile, options, selectedWindow);
            }
        }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally
        {
            _highlighter.Stop();
            RefreshAll();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback()
    {
        _highlighter.Stop();
        _restartSession.EmergencyStop();
        _player.EmergencyStop();
        SetStatus("已请求紧急停止并释放输入。");
    }

    /// <summary>
    /// Best-effort release of every held key/button for the dispatcher unhandled-exception
    /// handler: a UI-thread crash during playback must not leave the game with inputs stuck down.
    /// Never throws, because it runs while the process is already in a faulted state.
    /// </summary>
    public void EmergencyStopForCrash()
    {
        try
        {
            _highlighter.Stop();
            if (_recorder.IsRecording) _recorder.Abort("程序遇到未预期错误，本次录制已丢弃。");
            _restartSession.EmergencyStop();
            _player.EmergencyStop();
        }
        catch
        {
            // Swallow: crash recovery must not raise a second exception.
        }
    }

    private void OnPlaybackStopped(PlaybackStopResult result)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnPlaybackStopped(result)); return; }
        if (!result.IsUnexpected) return;
        var message = $"回放已停止：{result.Reason}";
        _recordingIndicator.ShowResult(message, false, TimeSpan.FromSeconds(5));
        _errorNotify?.Invoke("造梦盒子·造梦西游四辅助工具：回放停止", result.Reason);
        SetStatus(message);
    }
}
