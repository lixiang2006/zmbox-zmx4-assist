namespace ZmboxZmx4Assist.Services;

/// <summary>Physical screen coordinates; never WPF device-independent units.</summary>
public readonly record struct PhysicalRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Width > 0 && Height > 0;
}

public static class WindowHighlightGeometry
{
    public static IReadOnlyList<PhysicalRect> CreateBars(PhysicalRect target, int thickness = 4)
    {
        if (!target.IsValid) throw new ArgumentException("目标窗口矩形无效。", nameof(target));
        if (thickness < 1) throw new ArgumentOutOfRangeException(nameof(thickness));

        return new[]
        {
            new PhysicalRect(target.Left - thickness, target.Top - thickness, target.Right + thickness, target.Top),
            new PhysicalRect(target.Left - thickness, target.Bottom, target.Right + thickness, target.Bottom + thickness),
            new PhysicalRect(target.Left - thickness, target.Top, target.Left, target.Bottom),
            new PhysicalRect(target.Right, target.Top, target.Right + thickness, target.Bottom)
        };
    }
}
