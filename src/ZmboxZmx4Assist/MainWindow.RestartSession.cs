using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using WpfColor = System.Windows.Media.Color;

namespace ZmboxZmx4Assist;

// Automatic restart session: settings binding/validation, request construction and the
// window-binding hand-off after a relaunch.
public partial class MainWindow
{
    private void RestartSessionEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _syncingRestartSessionSettings) return;
        PersistRestartSessionSettingsIfValid();
    }

    private void RestartPreparationMacroCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingRestartSessionSettings) return;
        PersistRestartSessionSettingsIfValid();
    }

    private void RestartSessionSettings_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsLoaded || _syncingRestartSessionSettings) return;
        PersistRestartSessionSettingsIfValid();
    }

    private void SyncRestartSessionSettings()
    {
        _syncingRestartSessionSettings = true;
        try
        {
            var settings = _targetSettings.RestartSession;
            RestartSessionEnabledCheck.IsChecked = settings.Enabled;
            RestartIntervalMinutesText.Text = settings.IntervalMinutes.ToString();
            RestartStartupBufferSecondsText.Text = settings.StartupBufferSeconds.ToString();
            RestartPreparationMacroCombo.SelectedItem = settings.PreparationMacroId is { } id
                ? _macros.FirstOrDefault(item => item.Macro.Id == id)
                : null;
        }
        finally
        {
            _syncingRestartSessionSettings = false;
        }
        RefreshRestartSessionUi();
    }

    private bool PersistRestartSessionSettingsIfValid()
    {
        if (_syncingRestartSessionSettings) return true;
        if (!int.TryParse(RestartIntervalMinutesText.Text, out var intervalMinutes) ||
            !int.TryParse(RestartStartupBufferSecondsText.Text, out var startupBufferSeconds))
        {
            RefreshRestartSessionUi("自动重启的分钟数和启动缓冲都必须是整数。");
            return false;
        }

        var existing = _targetSettings.RestartSession;
        var selectedPreparationId = (RestartPreparationMacroCombo.SelectedItem as MacroListItem)?.Macro.Id;
        // Removing the saved preparation macro clears the ComboBox automatically.
        // Keep its ID on disk so the user sees a diagnosis instead of silently
        // falling back to a different macro on the next start.
        if (selectedPreparationId is null && existing.PreparationMacroId is { } staleId &&
            !_macros.Any(item => item.Macro.Id == staleId))
            selectedPreparationId = staleId;

        var updated = existing with
        {
            Enabled = RestartSessionEnabledCheck.IsChecked == true,
            IntervalMinutes = intervalMinutes,
            StartupBufferSeconds = startupBufferSeconds,
            PreparationMacroId = selectedPreparationId
        };
        if (updated.Validate() is { } validation)
        {
            RefreshRestartSessionUi(validation);
            return false;
        }

        _targetSettings = _targetSettings with { RestartSession = updated };
        _library.SaveZmboxTarget(_targetSettings);
        RefreshRestartSessionUi();
        return true;
    }

    private void RefreshRestartSessionUi(string? validationOverride = null)
    {
        if (!IsLoaded) return;
        var settings = _targetSettings.RestartSession;
        var busy = _recorder.IsRecording || _player.IsRunning || _restartSession.IsRunning;
        RestartSessionEnabledCheck.IsEnabled = !busy;
        RestartIntervalMinutesText.IsEnabled = !busy && settings.Enabled;
        RestartStartupBufferSecondsText.IsEnabled = !busy && settings.Enabled;
        RestartPreparationMacroCombo.IsEnabled = !busy && settings.Enabled;

        var selectedPreparation = settings.PreparationMacroId is { } id
            ? _macros.FirstOrDefault(item => item.Macro.Id == id)?.Macro
            : null;
        var message = validationOverride ?? settings.Validate();
        var warning = message is not null;
        if (!warning && !settings.Enabled)
            message = "已关闭；普通回放不会关闭或重启造梦盒子。";
        else if (!warning && settings.PreparationMacroId is null)
        {
            message = "启用后必须选择一个与主宏不同的准备宏。";
            warning = true;
        }
        else if (!warning && selectedPreparation is null)
        {
            message = "已保存的准备宏不存在或损坏，请重新选择。";
            warning = true;
        }
        else if (!warning && SelectedMacro?.Id == selectedPreparation?.Id)
        {
            message = "准备宏不能与当前主宏相同。";
            warning = true;
        }
        else if (!warning && (string.IsNullOrWhiteSpace(_targetSettings.ExecutablePath) || !File.Exists(_targetSettings.ExecutablePath)))
        {
            message = "尚未记忆可用的造梦盒子 EXE 路径；自动重启无法开始。";
            warning = true;
        }
        else if (!warning)
            message = $"主宏安全结束后，运行满 {settings.IntervalMinutes} 分钟会正常重启；启动后等待 {settings.StartupBufferSeconds} 秒并执行一次准备宏。";

        RestartSessionHintText.Text = message;
        RestartSessionHintText.Foreground = warning
            ? new SolidColorBrush(WpfColor.FromRgb(244, 180, 65))
            : new SolidColorBrush(WpfColor.FromRgb(142, 164, 181));
    }

    private RestartSessionRequest? CreateRestartSessionRequest(
        MacroDefinition primaryMacro,
        TargetProfile profile,
        PlaybackOptions options,
        ManualWindowBinding selectedWindow)
    {
        var settings = _targetSettings.RestartSession;
        var preparation = settings.PreparationMacroId is { } preparationId
            ? _macros.FirstOrDefault(item => item.Macro.Id == preparationId)?.Macro
            : null;
        if (preparation is null)
        {
            SetStatus("已启用自动重启，但准备宏不存在或已损坏；请在“自动重启会话”中重新选择。");
            return null;
        }

        var request = new RestartSessionRequest(
            primaryMacro,
            preparation,
            profile,
            options,
            selectedWindow,
            settings,
            profile.ExecutablePath);
        if (RestartSessionService.ValidateRequest(request) is { } validation)
        {
            SetStatus(validation);
            return null;
        }

        return request;
    }

    private void OnRestartSessionWindowBindingChanged(ManualWindowBinding binding)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnRestartSessionWindowBindingChanged(binding));
            return;
        }

        // A restart is part of the same explicitly approved session. Keep the
        // new PID as the current lock without showing another countdown.
        _manualWindow = binding;
        SelectedWindowText.Text =
            $"自动重启后已重新锁定窗口\n标题：{binding.Window.WindowTitle}\n进程：{binding.Window.ProcessName} · PID {binding.Window.ProcessId}\n" +
            $"位置与尺寸：({binding.Window.Layout.WindowX}, {binding.Window.Layout.WindowY}) · {binding.Window.Layout.WindowWidth} × {binding.Window.Layout.WindowHeight}\n开始后仅绑定此窗口。";
        RefreshPreflight();
    }
}
