using System.Windows;
using ZmboxZmx4Assist.Services;
using ZmboxZmx4Assist.ViewModels;

namespace ZmboxZmx4Assist.Views;

// Recording session: start/stop, save, and the recording-indicator notifications.
public partial class MainWindow
{
    private void RecordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording) { FinishRecording(); return; }
        if (_restartSession.IsRunning || _player.IsRunning)
        {
            // The global hotkey calls this method directly, bypassing the disabled button, so the
            // guard belongs here as well: recording while SendInput-driven playback runs would
            // capture injected events as if the user had typed them.
            SetStatus(_restartSession.IsRunning
                ? "自动重启会话正在运行；请先按紧急停止后再录制。"
                : "回放正在运行；请先按紧急停止后再录制。");
            return;
        }
        if (!_recorder.Start(_hotkeySettings)) { ShowRecordingResult("录制只能在目标窗口位于前台时启动。", false); return; }
        _recordingIndicator.ShowRecording(_hotkeySettings.RecordHotkey.DisplayText);
        _notify?.Invoke("造梦盒子·造梦西游四辅助工具", $"已开始录制；按 {_hotkeySettings.RecordHotkey.DisplayText} 结束并保存。");
        SetStatus("正在录制；切换离开目标窗口会自动丢弃本次录制。");
        RefreshControlState();
    }

    private void FinishRecording()
    {
        var window = _targets.Find();
        if (_recorder.WasAborted || window == IntPtr.Zero) { _recorder.Abort("录制已丢弃。"); RefreshControlState(); return; }
        var requestedName = MacroNameText.Text.Trim();
        var name = MacroLibraryService.ValidateMacroName(requestedName) is null ? requestedName : "未命名宏";
        var macro = _recorder.Stop(name, _targets.GetLayout(window));
        try
        {
            _library.SaveMacro(macro);
            var item = new MacroListItem(macro);
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

    private void OnRecordingStopped(string reason)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnRecordingStopped(reason)); return; }
        if (_recorder.WasTruncated)
        {
            // The event ceiling stopped the session; keep what was captured instead of discarding it.
            SetStatus(reason);
            FinishRecording();
            return;
        }
        if (_recorder.WasAborted) ShowRecordingResult(reason, false);
        else SetStatus(reason);
        RefreshControlState();
    }

    private void ShowRecordingResult(string message, bool success = true)
    {
        _recordingIndicator.ShowResult(message, success);
        _notify?.Invoke("造梦盒子·造梦西游四辅助工具", message);
        SetStatus(message);
    }
}
