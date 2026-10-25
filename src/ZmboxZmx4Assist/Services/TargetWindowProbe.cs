namespace ZmboxZmx4Assist.Services;

/// <summary>Production <see cref="ITargetStateProbe"/> over the real window service.</summary>
internal sealed class TargetWindowProbe(TargetWindowService targets) : ITargetStateProbe
{
    public bool IsWindow(IntPtr window) => targets.IsWindow(window);

    public bool IsForeground(IntPtr window) => targets.IsForeground(window);
}
