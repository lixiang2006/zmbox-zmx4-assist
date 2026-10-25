using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class MouseMoveSamplerTests
{
    [TestMethod]
    public void FirstMoveIsAlwaysKept()
    {
        var sampler = new MouseMoveSampler();
        Assert.IsTrue(sampler.ShouldKeep(10, 20, 0));
    }

    [TestMethod]
    public void TinyMoveWithinTheIntervalIsDropped()
    {
        var sampler = new MouseMoveSampler();
        Assert.IsTrue(sampler.ShouldKeep(10, 20, 0));
        Assert.IsFalse(sampler.ShouldKeep(11, 20, 1_000));
        Assert.IsFalse(sampler.ShouldKeep(11, 21, 2_000));
        Assert.IsFalse(sampler.ShouldKeep(10, 20, 7_999));
    }

    [TestMethod]
    public void MoveOfTwoPixelsIsKeptEvenImmediately()
    {
        var sampler = new MouseMoveSampler();
        Assert.IsTrue(sampler.ShouldKeep(10, 20, 0));
        Assert.IsTrue(sampler.ShouldKeep(12, 20, 500));
        Assert.IsTrue(sampler.ShouldKeep(12, 22, 900));
    }

    [TestMethod]
    public void SamePositionIsKeptAfterTheInterval()
    {
        var sampler = new MouseMoveSampler();
        Assert.IsTrue(sampler.ShouldKeep(10, 20, 0));
        Assert.IsTrue(sampler.ShouldKeep(10, 20, 8_000));
        Assert.IsFalse(sampler.ShouldKeep(10, 20, 8_001));
    }

    [TestMethod]
    public void NoticeMovesTheDistanceBaselineForClicksAndWheel()
    {
        var sampler = new MouseMoveSampler();
        Assert.IsTrue(sampler.ShouldKeep(0, 0, 0));
        sampler.Notice(50, 50, 1_000);
        Assert.IsFalse(sampler.ShouldKeep(51, 50, 1_500));
        Assert.IsTrue(sampler.ShouldKeep(52, 50, 1_600));
    }

    [TestMethod]
    public void DiagonalJitterStaysBelowTheTwoPixelRadius()
    {
        var sampler = new MouseMoveSampler();
        Assert.IsTrue(sampler.ShouldKeep(0, 0, 0));
        Assert.IsFalse(sampler.ShouldKeep(1, 1, 1_000));
        Assert.IsTrue(sampler.ShouldKeep(1, 2, 1_500));
    }
}
