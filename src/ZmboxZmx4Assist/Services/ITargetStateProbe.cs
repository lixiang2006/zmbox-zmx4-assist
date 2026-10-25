namespace ZmboxZmx4Assist.Services;

/// <summary>
/// The cheapest liveness questions the per-event pause decision needs: does the handle still exist,
/// and does it still own the foreground. Layout, process identity and background capability are
/// expensive to ask and are answered by a full check on its own schedule.
/// </summary>
internal interface ITargetStateProbe
{
    bool IsWindow(IntPtr window);

    bool IsForeground(IntPtr window);
}
