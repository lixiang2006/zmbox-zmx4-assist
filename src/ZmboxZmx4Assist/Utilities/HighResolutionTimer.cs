using ZmboxZmx4Assist.Interop;

namespace ZmboxZmx4Assist.Utilities;

/// <summary>
/// Raises the system timer resolution to 1 ms while playback runs. Without it, Task.Delay and
/// the playback wait loop quantise to the default ~15.6 ms tick, so recorded pauses drift and
/// dense macros replay visibly unevenly. Nested acquisitions only touch the timer on the
/// outermost begin/end pair, because a restart session runs playback rounds back to back.
/// </summary>
public static class HighResolutionTimer
{
    private const uint PeriodMilliseconds = 1;
    private static int _depth;

    internal static int ActiveDepth => Volatile.Read(ref _depth);

    public static IDisposable Acquire()
    {
        if (Interlocked.Increment(ref _depth) == 1) NativeMethods.timeBeginPeriod(PeriodMilliseconds);
        return new Handle();
    }

    private sealed class Handle : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (Interlocked.Decrement(ref _depth) == 0) NativeMethods.timeEndPeriod(PeriodMilliseconds);
        }
    }
}
