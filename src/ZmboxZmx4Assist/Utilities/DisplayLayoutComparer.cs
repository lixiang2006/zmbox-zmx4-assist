using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Utilities;

public static class DisplayLayoutComparer
{
    public const int WindowTolerancePixels = 8;
    private const int UnavailableWindowCoordinate = -32_000;

    public static bool HasUsableWindowBounds(DisplayLayout layout) =>
        layout.WindowWidth > 0 &&
        layout.WindowHeight > 0 &&
        !(layout.WindowX <= UnavailableWindowCoordinate && layout.WindowY <= UnavailableWindowCoordinate);

    public static LayoutComparison Compare(DisplayLayout? expected, DisplayLayout actual, int tolerancePixels = WindowTolerancePixels)
    {
        if (expected is null)
            return new LayoutComparison(false, "宏缺少录制时的显示布局。");
        if (!HasUsableWindowBounds(actual))
            return new LayoutComparison(false, "目标窗口暂时没有可用布局，可能正在切换、最小化或重建。", IsTransient: true);
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return new LayoutComparison(false, $"主屏分辨率已由 {expected.Width}×{expected.Height} 变为 {actual.Width}×{actual.Height}。");
        if (expected.Dpi != actual.Dpi)
            return new LayoutComparison(false, $"主屏缩放 DPI 已由 {expected.Dpi} 变为 {actual.Dpi}。");

        var xDifference = actual.WindowX - expected.WindowX;
        var yDifference = actual.WindowY - expected.WindowY;
        var widthDifference = actual.WindowWidth - expected.WindowWidth;
        var heightDifference = actual.WindowHeight - expected.WindowHeight;
        if (Math.Abs(xDifference) > tolerancePixels || Math.Abs(yDifference) > tolerancePixels || Math.Abs(widthDifference) > tolerancePixels || Math.Abs(heightDifference) > tolerancePixels)
        {
            return new LayoutComparison(
                false,
                $"窗口变化超出 ±{tolerancePixels}px 容差（位置 {xDifference:+#;-#;0}/{yDifference:+#;-#;0}px，尺寸 {widthDifference:+#;-#;0}/{heightDifference:+#;-#;0}px）。",
                xDifference,
                yDifference,
                widthDifference,
                heightDifference);
        }

        return new LayoutComparison(true, $"窗口布局符合 ±{tolerancePixels}px 容差。", xDifference, yDifference, widthDifference, heightDifference);
    }
}
