using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

/// <summary>
/// The seam between the playback timeline and the actual input delivery. The production
/// implementation injects into Windows; a recording implementation lets tests assert the exact
/// dispatch sequence without touching the desktop.
/// </summary>
public interface IInputSink
{
    /// <summary>Delivers one recorded event through the foreground or background channel.</summary>
    void Dispatch(IntPtr window, RecordedEvent e, PlaybackMode mode, VirtualScreenBounds bounds);

    /// <summary>Best-effort release of an input that is still held when playback stops.</summary>
    void Release(IntPtr window, RecordedEvent e, PlaybackMode mode);

    /// <summary>
    /// Checks once before a background playback that this process is actually allowed to post window
    /// messages to the target. Windows rejects cross-integrity posts, so probing up front turns a
    /// mystery mid-macro abort into a precise, actionable message.
    /// </summary>
    bool TryProbeBackgroundChannel(IntPtr window, out string reason);
}
