using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class WindowHighlightGeometryTests
{
    [TestMethod]
    public void CreateBars_PlacesFourBarsOutsideTheTargetInPhysicalPixels()
    {
        var bars = WindowHighlightGeometry.CreateBars(new PhysicalRect(100, 200, 500, 600));

        CollectionAssert.AreEqual(new[]
        {
            new PhysicalRect(96, 196, 504, 200),
            new PhysicalRect(96, 600, 504, 604),
            new PhysicalRect(96, 200, 100, 600),
            new PhysicalRect(500, 200, 504, 600)
        }, bars.ToArray());
    }

    [TestMethod]
    public void CreateBars_RejectsInvalidThickness()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => WindowHighlightGeometry.CreateBars(new PhysicalRect(1, 1, 2, 2), 0));
    }

    [TestMethod]
    public void CreateBars_RefreshUsesTheNewWindowPosition()
    {
        var bars = WindowHighlightGeometry.CreateBars(new PhysicalRect(400, 300, 700, 500));

        Assert.AreEqual(new PhysicalRect(396, 296, 704, 300), bars[0]);
        Assert.AreEqual(new PhysicalRect(700, 300, 704, 500), bars[3]);
    }
}
