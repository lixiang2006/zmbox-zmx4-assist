using System.Windows;
using System.Windows.Controls;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist;

// Target window: profile edits, manual window lock/capture, background capability marking and
// the pre-flight/preview panels.
public partial class MainWindow
{
    private void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profile = SelectedProfile;
        ProfileNameText.Text = profile.Name;
        ExecutablePathText.Text = profile.ExecutablePath;
        WindowTitleText.Text = profile.WindowTitleContains;
        RefreshAll();
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
        // TargetProfile is a value-equal record: callers such as MarkBackground() mutate
        // _targetSettings before calling this, so IndexOf(SelectedProfile) can no longer match
        // the stored instance. Match by Id instead and fall back to appending.
        var index = -1;
        for (var i = 0; i < _profiles.Count; i++)
        {
            if (_profiles[i].Id != replacement.Id) continue;
            index = i;
            break;
        }
        if (index >= 0) _profiles[index] = replacement;
        else _profiles.Add(replacement);
        ProfilesCombo.SelectedItem = replacement;
        foreach (var item in _macros) item.Replace(item.Macro, ProfileNameFor(item.Macro.TargetProfileId));
        _library.SaveProfiles(_profiles);
        RefreshAll();
    }

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
            BackgroundCapability = updated.BackgroundCapability,
            RestartSession = _targetSettings.RestartSession
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
}
