using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class PlaybackTimingTests
{
    [TestMethod]
    public void HighResolutionTimer_NestedAcquisitions_OnlyReleaseOnOutermostDispose()
    {
        var before = HighResolutionTimer.ActiveDepth;
        var outer = HighResolutionTimer.Acquire();
        Assert.AreEqual(before + 1, HighResolutionTimer.ActiveDepth);
        using (HighResolutionTimer.Acquire())
        {
            Assert.AreEqual(before + 2, HighResolutionTimer.ActiveDepth);
        }
        Assert.AreEqual(before + 1, HighResolutionTimer.ActiveDepth);
        outer.Dispose();
        Assert.AreEqual(before, HighResolutionTimer.ActiveDepth);
    }

    [TestMethod]
    public void HighResolutionTimer_DoubleDispose_IsIdempotent()
    {
        var before = HighResolutionTimer.ActiveDepth;
        var handle = HighResolutionTimer.Acquire();

        handle.Dispose();
        handle.Dispose();

        Assert.AreEqual(before, HighResolutionTimer.ActiveDepth);
    }
}
