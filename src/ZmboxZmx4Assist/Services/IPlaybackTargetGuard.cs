using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Services;

/// <summary>Result of one target-readiness question, including whether the window is being rebuilt.</summary>
public sealed record TargetReadiness(IntPtr Window, bool IsReady, string Reason, bool IsTransient = false);

/// <summary>
/// Seam between the playback timeline and the live target-window rules. The production guard
/// answers from Win32; tests can answer "ready" to exercise the timeline without a real window.
/// </summary>
public interface IPlaybackTargetGuard
{
    /// <summary>Resolves the window to play into, rebinding inside the locked process if needed.</summary>
    TargetReadiness Find(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow);

    /// <summary>Checks one specific window, for the per-event pause/recovery decision.</summary>
    TargetReadiness Check(IntPtr window, MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow);
}
