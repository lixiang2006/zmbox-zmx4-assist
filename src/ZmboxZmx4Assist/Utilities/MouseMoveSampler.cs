namespace ZmboxZmx4Assist.Utilities;

/// <summary>
/// Decides which raw mouse-move samples are worth persisting. Windows delivers up to ~1 kHz of
/// WM_MOUSEMOVE while the pointer travels, but a replay only needs the moves that change the
/// path meaningfully; keeping every sample turns a few minutes of play into tens of thousands of
/// events, bloats the macro file and slows both loading and playback.
/// </summary>
public sealed class MouseMoveSampler
{
    /// <summary>Keep a move when this long has passed since the last kept move.</summary>
    public const long MinimumIntervalMicroseconds = 8_000;

    /// <summary>Keep a move when it is at least this far from the last kept position.</summary>
    public const int MinimumDistancePixels = 2;

    private bool _hasPrevious;
    private int _previousX;
    private int _previousY;
    private long _lastKeptMicroseconds;

    public void Reset()
    {
        _hasPrevious = false;
        _previousX = 0;
        _previousY = 0;
        _lastKeptMicroseconds = 0;
    }

    /// <summary>
    /// Notes a non-move mouse event (click, wheel) so the next move is measured from where the
    /// pointer actually was when it happened, not from an older sampled position.
    /// </summary>
    public void Notice(int x, int y, long offsetMicroseconds)
    {
        _hasPrevious = true;
        _previousX = x;
        _previousY = y;
        _lastKeptMicroseconds = offsetMicroseconds;
    }

    /// <summary>Whether this mouse-move sample should be recorded.</summary>
    public bool ShouldKeep(int x, int y, long offsetMicroseconds)
    {
        if (!_hasPrevious) { Remember(x, y, offsetMicroseconds); return true; }

        var dx = x - _previousX;
        var dy = y - _previousY;
        if ((long)dx * dx + (long)dy * dy >= (long)MinimumDistancePixels * MinimumDistancePixels)
        {
            Remember(x, y, offsetMicroseconds);
            return true;
        }

        if (offsetMicroseconds - _lastKeptMicroseconds >= MinimumIntervalMicroseconds)
        {
            Remember(x, y, offsetMicroseconds);
            return true;
        }

        return false;
    }

    private void Remember(int x, int y, long offsetMicroseconds)
    {
        _hasPrevious = true;
        _previousX = x;
        _previousY = y;
        _lastKeptMicroseconds = offsetMicroseconds;
    }
}
