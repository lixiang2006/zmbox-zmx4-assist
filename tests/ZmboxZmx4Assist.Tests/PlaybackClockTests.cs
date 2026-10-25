using System.Diagnostics;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Tests;

/// <summary>
/// 回放时钟的两条不变量：熬夜醒来超过容忍值要补偿（否则后面的间隔会被压成一串连发），
/// 未超过容忍值的抖动是正常的自旋余量，不参与补偿。
/// </summary>
[TestClass]
public sealed class PlaybackClockTests
{
    [TestMethod]
    public void Overshoot_IsAbsorbedOnlyBeyondTheTolerance()
    {
        var tolerance = PlaybackService.OvershootToleranceTicks;

        Assert.AreEqual(0L, PlaybackService.OvershootTicks(0, 0, tolerance));
        Assert.AreEqual(0L, PlaybackService.OvershootTicks(0, 0, tolerance - 1));
        var late = tolerance * 4;
        Assert.AreEqual(late, PlaybackService.OvershootTicks(0, 0, late));
    }

    [TestMethod]
    public void Overshoot_IsMeasuredAgainstTheScheduledDueTime()
    {
        var tolerance = PlaybackService.OvershootToleranceTicks;
        const long started = 10_000;
        const long due = 5_000;

        Assert.AreEqual(0L, PlaybackService.OvershootTicks(started, due, started + due + tolerance));
        Assert.AreEqual(tolerance * 3, PlaybackService.OvershootTicks(started, due, started + due + tolerance * 3));
    }

    [TestMethod]
    public void DueTicks_ConvertMicrosecondsToStopwatchTicks()
    {
        Assert.AreEqual(0L, PlaybackService.DueTicks(0));
        Assert.IsTrue(Math.Abs(PlaybackService.DueTicks(1_000_000d) - Stopwatch.Frequency) <= 1);
        Assert.IsTrue(Math.Abs(PlaybackService.DueTicks(500_000d) - Stopwatch.Frequency / 2.0) <= 1);
    }
}
