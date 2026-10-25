namespace ZmboxZmx4Assist.Utilities;

/// <summary>The physical-pixel rectangle of the whole virtual desktop, in screen coordinates.</summary>
public readonly record struct VirtualScreenBounds(int Left, int Top, int Width, int Height);

/// <summary>
/// Converts absolute screen pixels to the 0..65535 normalised space used by
/// <c>MOUSEEVENTF_ABSOLUTE</c>. Windows always normalises against the whole virtual desktop,
/// so coordinates must never be divided by the primary screen size: a window on a secondary
/// monitor (or a monitor arranged above/left of the primary one) would otherwise receive
/// clicks in the wrong place.
/// </summary>
public static class MouseCoordinateTranslator
{
    public const int AbsoluteMaximum = 65535;

    public static (int Dx, int Dy) ToAbsolute(int screenX, int screenY, VirtualScreenBounds bounds)
    {
        var width = Math.Max(2, bounds.Width);
        var height = Math.Max(2, bounds.Height);
        var dx = Clamp((screenX - bounds.Left) * (long)AbsoluteMaximum / (width - 1));
        var dy = Clamp((screenY - bounds.Top) * (long)AbsoluteMaximum / (height - 1));
        return ((int)dx, (int)dy);
    }

    private static long Clamp(long value) => value < 0 ? 0 : value > AbsoluteMaximum ? AbsoluteMaximum : value;
}
