using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist;

public sealed class MacroListItem : INotifyPropertyChanged
{
    private MacroDefinition _macro;
    private string _profileName;
    private string _draftName;
    private bool _isRenaming;

    public MacroListItem(MacroDefinition macro, string profileName)
    {
        _macro = macro;
        _profileName = profileName;
        _draftName = macro.Name;
    }

    public MacroDefinition Macro => _macro;
    public string Name => _macro.Name;
    public string DraftName { get => _draftName; set { _draftName = value; OnPropertyChanged(); } }
    public bool IsRenaming { get => _isRenaming; private set { _isRenaming = value; OnPropertyChanged(); } }
    public string DetailLine => $"{_profileName}  ·  {_macro.Events.Count} 个事件  ·  {FormatDuration(_macro.Events)}";

    public void BeginRename() { DraftName = _macro.Name; IsRenaming = true; }
    public void CancelRename() { DraftName = _macro.Name; IsRenaming = false; }
    public void Replace(MacroDefinition macro, string profileName)
    {
        _macro = macro;
        _profileName = profileName;
        _draftName = macro.Name;
        IsRenaming = false;
        OnPropertyChanged(nameof(Macro));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DetailLine));
    }

    private static string FormatDuration(IReadOnlyList<RecordedEvent> events)
    {
        var duration = events.Count == 0 ? TimeSpan.Zero : TimeSpan.FromMicroseconds(events.Max(x => x.OffsetMicroseconds));
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
