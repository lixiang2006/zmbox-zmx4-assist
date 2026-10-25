using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Tests;

/// <summary>
/// 每个回放事件都会问一次“目标窗口还能用吗”。这些测试钉住新的节流策略：
/// 便宜的存活/前台检查每个事件都做，完整检查（进程身份、布局、DPI）按间隔复用。
/// </summary>
[TestClass]
public sealed class TargetStateMonitorTests
{
    private static readonly DisplayLayout Layout = new(1920, 1080, 96, 0, 0, 1920, 1080);
    private static readonly IntPtr FirstWindow = new(0x1234);
    private static readonly IntPtr SecondWindow = new(0x5678);

    private long _now = 1_000;

    [TestMethod]
    public void Check_ReusesTheFullCheckWithinTheInterval()
    {
        var guard = new FakeGuard();
        var monitor = BuildMonitor(guard, new FakeProbe(), interval: 100);

        var first = monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);
        _now += 99;
        var second = monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);

        Assert.IsTrue(first.IsReady);
        Assert.IsTrue(second.IsReady);
        Assert.AreEqual(1, guard.Checks);
    }

    [TestMethod]
    public void Check_RevalidatesAfterTheInterval()
    {
        var guard = new FakeGuard();
        var monitor = BuildMonitor(guard, new FakeProbe(), interval: 100);

        monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);
        _now += 100;
        monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);

        Assert.AreEqual(2, guard.Checks);
    }

    [TestMethod]
    public void Check_PausesImmediatelyWhenTheWindowDisappears()
    {
        var guard = new FakeGuard();
        var probe = new FakeProbe { WindowAlive = false };
        var monitor = BuildMonitor(guard, probe);

        var readiness = monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);

        Assert.IsFalse(readiness.IsReady);
        StringAssert.Contains(readiness.Reason, "句柄已失效");
        Assert.AreEqual(0, guard.Checks);
    }

    [TestMethod]
    public void Check_PausesImmediatelyWhenTheForegroundMoved()
    {
        var guard = new FakeGuard();
        var probe = new FakeProbe { Foreground = false };
        var monitor = BuildMonitor(guard, probe);

        var readiness = monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);

        Assert.IsFalse(readiness.IsReady);
        StringAssert.Contains(readiness.Reason, "已切离");
        Assert.AreEqual(0, guard.Checks);
    }

    [TestMethod]
    public void Check_DoesNotCacheAFailedFullCheck()
    {
        var guard = new FakeGuard { CheckResult = new TargetReadiness(FirstWindow, false, "目标窗口尚未回到前台。") };
        var monitor = BuildMonitor(guard, new FakeProbe(), interval: 100);

        var first = monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);
        _now += 1;
        var second = monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);

        Assert.IsFalse(first.IsReady);
        Assert.IsFalse(second.IsReady);
        Assert.AreEqual(2, guard.Checks);
    }

    [TestMethod]
    public void Check_RunsAFullCheckWhenTheWindowHandleChanges()
    {
        var guard = new FakeGuard();
        var monitor = BuildMonitor(guard, new FakeProbe(), interval: 100);

        monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);
        _now += 1;
        monitor.Check(SecondWindow, Macro(), TargetSettings(), ForegroundOptions(), null);

        Assert.AreEqual(2, guard.Checks);
    }

    [TestMethod]
    public void Find_SeedsTheCache()
    {
        var guard = new FakeGuard();
        var monitor = BuildMonitor(guard, new FakeProbe(), interval: 100);

        var found = monitor.Find(Macro(), TargetSettings(), ForegroundOptions(), null);
        var checkedAgain = monitor.Check(FirstWindow, Macro(), TargetSettings(), ForegroundOptions(), null);

        Assert.IsTrue(found.IsReady);
        Assert.IsTrue(checkedAgain.IsReady);
        Assert.AreEqual(0, guard.Checks, "Find 已经把完整检查的结果放进缓存，紧接着的 Check 不应再查一次。");
    }

    [TestMethod]
    public void Check_PausesImmediatelyWhenABackgroundWindowIsNotTheSelectedTargetSettings()
    {
        var guard = new FakeGuard();
        var monitor = BuildMonitor(guard, new FakeProbe());
        var binding = new ManualWindowBinding(
            new WindowCandidate(FirstWindow, 37768, "造梦盒子", "D:\\game\\造梦盒子.exe", "造梦盒子v1.8", Layout),
            MatchesTarget: false);
        var options = PlaybackOptions.Default with { Mode = PlaybackMode.ExperimentalTargetWindow };

        var readiness = monitor.Check(FirstWindow, Macro(), TargetSettings(), options, binding);

        Assert.IsFalse(readiness.IsReady);
        StringAssert.Contains(readiness.Reason, "不匹配");
        Assert.AreEqual(0, guard.Checks);
    }

    private TargetStateMonitor BuildMonitor(FakeGuard guard, FakeProbe probe, long interval = -1) =>
        new(guard, probe, () => _now, interval);

    private static MacroDefinition Macro() => new()
    {
        Name = "节流测试",
        TargetProfileId = ZmboxTargetSettings.TargetProfileId,
        DisplayLayout = Layout,
        Events = [new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = 0x57 }]
    };

    private static ZmboxTargetSettings TargetSettings() => new()
    {
        WindowTitleContains = "造梦盒子",
        BackgroundCapability = BackgroundCapability.Verified
    };

    private static PlaybackOptions ForegroundOptions() => PlaybackOptions.Default with { Mode = PlaybackMode.ForegroundSystemInput };

    private sealed class FakeGuard : IPlaybackTargetGuard
    {
        public int Checks { get; private set; }

        public TargetReadiness CheckResult { get; init; } = new(FirstWindow, true, string.Empty);

        public TargetReadiness FindResult { get; init; } = new(FirstWindow, true, string.Empty);

        public TargetReadiness Find(MacroDefinition macro, ZmboxTargetSettings target, PlaybackOptions options, ManualWindowBinding? manualWindow) =>
            FindResult with { Window = manualWindow?.Window.Handle ?? FindResult.Window };

        public TargetReadiness Check(IntPtr window, MacroDefinition macro, ZmboxTargetSettings target, PlaybackOptions options, ManualWindowBinding? manualWindow)
        {
            Checks++;
            return CheckResult with { Window = window };
        }
    }

    private sealed class FakeProbe : ITargetStateProbe
    {
        public bool WindowAlive { get; init; } = true;

        public bool Foreground { get; init; } = true;

        public bool IsWindow(IntPtr window) => WindowAlive;

        public bool IsForeground(IntPtr window) => Foreground;
    }
}
