using System.Text.Json.Serialization;
using System.Windows.Input;

namespace ZmboxZmx4Assist.Domain;

public enum InputEventKind
{
    KeyDown, KeyUp, MouseMove, MouseDown, MouseUp, MouseWheel
}

public enum MouseButtonKind { None, Left, Right, Middle, X1, X2 }
public enum PlaybackMode { ForegroundSystemInput, ExperimentalTargetWindow }
public enum BackgroundCapability { Unknown, Verified, Unsupported }

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

[JsonConverter(typeof(HotkeyBindingJsonConverter))]
public sealed record HotkeyBinding(uint VirtualKey = 0, HotkeyModifiers Modifiers = HotkeyModifiers.None)
{
    public static HotkeyBinding RecordDefault { get; } = new(0x77);
    public static HotkeyBinding PlayDefault { get; } = new(0x78);
    public static HotkeyBinding EmergencyDefault { get; } = new(0x7B);

    public bool IsValid => VirtualKey != 0 && !IsModifierVirtualKey(VirtualKey) && (Modifiers & ~SupportedModifiers) == 0;

    public bool InvolvesVirtualKey(uint virtualKey) =>
        virtualKey == VirtualKey ||
        (Modifiers.HasFlag(HotkeyModifiers.Control) && virtualKey is 0x11 or 0xA2 or 0xA3) ||
        (Modifiers.HasFlag(HotkeyModifiers.Shift) && virtualKey is 0x10 or 0xA0 or 0xA1) ||
        (Modifiers.HasFlag(HotkeyModifiers.Alt) && virtualKey is 0x12 or 0xA4 or 0xA5) ||
        (Modifiers.HasFlag(HotkeyModifiers.Windows) && virtualKey is 0x5B or 0x5C);

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(4);
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
            parts.Add(KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString());
            return string.Join(" + ", parts);
        }
    }

    public static bool IsModifierVirtualKey(uint virtualKey) => virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private const HotkeyModifiers SupportedModifiers = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows;
}

public sealed record DisplayLayout(int Width, int Height, int Dpi, int WindowX, int WindowY, int WindowWidth, int WindowHeight);

public sealed record LayoutComparison(
    bool IsMatch,
    string Reason,
    int XDifference = 0,
    int YDifference = 0,
    int WidthDifference = 0,
    int HeightDifference = 0,
    bool IsTransient = false);

public enum PlaybackStopKind { UserRequested, RecoveryTimedOut, InputFailure, RestartFailure }

public sealed record PlaybackStopResult(PlaybackStopKind Kind, string Reason)
{
    public bool IsUnexpected => Kind != PlaybackStopKind.UserRequested;
}

public sealed record RecordedEvent
{
    public long OffsetMicroseconds { get; init; }
    public InputEventKind Kind { get; init; }
    public int VirtualKey { get; init; }
    public MouseButtonKind Button { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int WheelDelta { get; init; }
}

/// <summary>The single supported launcher target for this application.</summary>
public sealed record ZmboxTargetSettings
{
    public static Guid TargetProfileId { get; } = Guid.Parse("c5079121-4f52-4f46-9202-75f6f9c3ce04");
    public const string DisplayName = "造梦盒子 · 造梦西游四";
    public string ExecutablePath { get; init; } = string.Empty;
    public string WindowTitleContains { get; init; } = "造梦盒子";
    public BackgroundCapability BackgroundCapability { get; init; } = BackgroundCapability.Unknown;
    public RestartSessionSettings RestartSession { get; init; } = new();
}

/// <summary>
/// Local-only settings for the optional safe restart session. Missing values in
/// older target.json files intentionally deserialize to these disabled defaults.
/// </summary>
public sealed record RestartSessionSettings
{
    public const int DefaultIntervalMinutes = 120;
    public const int DefaultStartupBufferSeconds = 60;
    public const int MinimumIntervalMinutes = 1;
    public const int MaximumIntervalMinutes = 1_440;
    public const int MaximumStartupBufferSeconds = 900;

    public bool Enabled { get; init; }
    public int IntervalMinutes { get; init; } = DefaultIntervalMinutes;
    public Guid? PreparationMacroId { get; init; }
    public int StartupBufferSeconds { get; init; } = DefaultStartupBufferSeconds;

    public string? Validate()
    {
        if (IntervalMinutes is < MinimumIntervalMinutes or > MaximumIntervalMinutes)
            return $"自动重启周期必须在 {MinimumIntervalMinutes} 到 {MaximumIntervalMinutes} 分钟之间。";
        if (StartupBufferSeconds is < 0 or > MaximumStartupBufferSeconds)
            return $"启动缓冲必须在 0 到 {MaximumStartupBufferSeconds} 秒之间。";
        return null;
    }
}

/// <summary>
/// A visible top-level window selected by the user for one playback session.
/// This is deliberately runtime-only and is never written into macro JSON.
/// </summary>
public sealed record WindowCandidate(
    IntPtr Handle,
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    string WindowTitle,
    DisplayLayout Layout,
    DateTime? ProcessStartedAtUtc = null)
{
    public string ProcessLine => string.IsNullOrWhiteSpace(ExecutablePath)
        ? $"{ProcessName} · PID {ProcessId} · 路径未读取"
        : $"{ProcessName} · PID {ProcessId} · {ExecutablePath}";

    public string LayoutLine => $"{Layout.WindowWidth} × {Layout.WindowHeight} · ({Layout.WindowX}, {Layout.WindowY})";
}

public sealed record ManualWindowBinding(WindowCandidate Window, bool MatchesTarget)
{
    public string Summary => $"{Window.WindowTitle} · {Window.ProcessName} (PID {Window.ProcessId})";
}

public sealed record MacroDefinition
{
    /// <summary>
    /// Version 2 stores events as compact positional arrays; version 1 object events are still read.
    /// </summary>
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "未命名宏";
    public Guid TargetProfileId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DisplayLayout? DisplayLayout { get; init; }
    public IReadOnlyList<RecordedEvent> Events { get; init; } = Array.Empty<RecordedEvent>();
}

public sealed record MacroLoadIssue(string FileName, string Message);

public sealed record MacroLoadResult(IReadOnlyList<MacroDefinition> Macros, IReadOnlyList<MacroLoadIssue> Issues);

public sealed record TargetPreflightResult(
    bool TargetFound,
    string WindowTitle,
    bool IsForeground,
    bool LayoutMatches,
    bool BackgroundAllowed,
    string Message);

public sealed record PlaybackOptions(
    int RepeatCount,
    bool InfiniteLoop,
    double SpeedMultiplier,
    PlaybackMode Mode,
    int InterIterationDelaySeconds,
    int CooldownEveryIterations,
    int CooldownSeconds)
{
    public static PlaybackOptions Default => new(1, false, 1.0, PlaybackMode.ForegroundSystemInput, 15, 10, 60);

    public bool ShouldCooldownAfter(int completedIterations, bool hasNextIteration) =>
        hasNextIteration && CooldownEveryIterations > 0 && CooldownSeconds > 0 && completedIterations % CooldownEveryIterations == 0;
}

public sealed record PlaybackRunResult(bool Succeeded, PlaybackStopResult? StopResult = null)
{
    public static PlaybackRunResult Completed { get; } = new(true);
}

public sealed record RestartSessionRequest(
    MacroDefinition PrimaryMacro,
    MacroDefinition PreparationMacro,
    ZmboxTargetSettings Target,
    PlaybackOptions PlaybackOptions,
    ManualWindowBinding InitialWindow,
    RestartSessionSettings Settings,
    string ExecutablePath);

public sealed record HotkeySettings
{
    public HotkeyBinding RecordHotkey { get; init; } = HotkeyBinding.RecordDefault;
    public HotkeyBinding PlayHotkey { get; init; } = HotkeyBinding.PlayDefault;
    public HotkeyBinding EmergencyHotkey { get; init; } = HotkeyBinding.EmergencyDefault;

    public bool IsControlKey(uint virtualKey) =>
        RecordHotkey.InvolvesVirtualKey(virtualKey) ||
        PlayHotkey.InvolvesVirtualKey(virtualKey) ||
        EmergencyHotkey.InvolvesVirtualKey(virtualKey);

    public string? Validate()
    {
        if (!RecordHotkey.IsValid || !PlayHotkey.IsValid || !EmergencyHotkey.IsValid)
            return "每个热键都必须包含一个非修饰键。";
        if (RecordHotkey == PlayHotkey || RecordHotkey == EmergencyHotkey || PlayHotkey == EmergencyHotkey)
            return "录制、回放和紧急停止热键不能重复。";
        return null;
    }
}

[JsonSerializable(typeof(MacroDefinition))]
[JsonSerializable(typeof(HotkeySettings))]
public partial class MacroJsonContext : JsonSerializerContext { }
