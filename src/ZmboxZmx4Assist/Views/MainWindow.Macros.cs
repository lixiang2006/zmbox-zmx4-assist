using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using ZmboxZmx4Assist.ViewModels;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ZmboxZmx4Assist.Views;

// Macro library: loading the saved macros, the list/summary refresh, rename and delete flows.
public partial class MainWindow
{
    private void LoadLibrary()
    {
        _targetSettings = _library.LoadZmboxTarget();
        var load = _library.LoadMacrosWithIssues();
        RebuildMacroItems(load.Macros);
        MacroIssueText.Visibility = load.Issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        MacroIssueText.Text = load.Issues.Count == 0 ? string.Empty : $"已跳过 {load.Issues.Count} 个损坏宏文件：{string.Join("、", load.Issues.Take(3).Select(x => x.FileName))}。原文件未移动或删除。";
        _hotkeySettings = _library.LoadHotkeys();
        SyncHotkeyDrafts(_hotkeySettings);
        var hotkeyResult = _hotkeys.Configure(_hotkeySettings);
        MacrosList.ItemsSource = _macros;
        RestartPreparationMacroCombo.ItemsSource = _macros;
        if (_macros.Count > 0) MacrosList.SelectedIndex = 0;
        SyncRestartSessionSettings();
        if (!hotkeyResult.AllRegistered) SetStatus(hotkeyResult.Message);
        RefreshAll();
    }

    private void RebuildMacroItems(IEnumerable<MacroDefinition> macros)
    {
        _macros.Clear();
        foreach (var macro in macros) _macros.Add(new MacroListItem(macro));
    }

    private void MacrosList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A locked Zmbox instance belongs to the playback session, not to one
        // macro. Switching macros must not force the user through capture again.
        SelectedMacroText.Text = SelectedMacroItem is { } item ? $"已选：{item.DetailLine}" : "新录制将保存为下列名称";
        RefreshAll();
    }

    private void RefreshMacroSummary()
    {
        MacroCountText.Text = _macros.Count == 0 ? "尚无宏；输入名称后开始录制" : $"共 {_macros.Count} 个本地宏";
        SelectedMacroText.Text = SelectedMacroItem is { } item ? $"已选：{item.DetailLine}" : "新录制将保存为下列名称";
    }

    private void DeleteMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMacroItem is not { } item) return;
        var answer = WpfMessageBox.Show($"确定删除“{item.Name}”？\n\n{item.DetailLine}\n此操作会删除本机宏文件。", "删除宏", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _library.DeleteMacro(item.Macro);
        _syncingRestartSessionSettings = true;
        try { _macros.Remove(item); }
        finally { _syncingRestartSessionSettings = false; }
        SetStatus($"已删除“{item.Name}”。");
        RefreshAll();
    }

    private void MacroName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not MacroListItem item || _recorder.IsRecording || _player.IsRunning || _restartSession.IsRunning) return;
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
            item.Replace(renamed);
            SetStatus($"已将宏改名为“{renamed.Name}”。");
        }
        catch (Exception ex)
        {
            item.CancelRename();
            SetStatus(ex.Message);
        }
    }

    private void OpenMacrosFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = _library.MacrosDirectory, UseShellExecute = true });
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
