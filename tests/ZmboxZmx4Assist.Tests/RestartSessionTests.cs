using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class RestartSessionTests
{
    [TestMethod]
    public async Task Session_RestartsOnlyAfterSafePrimaryBoundary_ThenRunsPreparationOnce()
    {
        using var fixture = new RestartFixture();
        var primaryRuns = 0;
        var executor = new FakeRoundExecutor(macro =>
        {
            if (macro.Id == fixture.Primary.Id && ++primaryRuns == 1) fixture.Clock.Advance(TimeSpan.FromMinutes(121));
            return PlaybackRunResult.Completed;
        });
        var lifecycle = fixture.CreateSuccessfulLifecycle(newProcessId: 2002);
        var session = new RestartSessionService(executor, lifecycle, fixture.Clock, new AdvancingDelay(fixture.Clock));
        ManualWindowBinding? rebound = null;
        session.WindowBindingChanged += binding => rebound = binding;

        await session.PlayAsync(fixture.Request(repeatCount: 2));

        CollectionAssert.AreEqual(new[] { fixture.Primary.Name, fixture.Preparation.Name, fixture.Primary.Name }, executor.ExecutedMacros);
        Assert.AreEqual(1, lifecycle.CloseCalls);
        Assert.AreEqual(fixture.InitialWindow.Window.ProcessId, lifecycle.ClosedProcessId);
        Assert.AreEqual(1, lifecycle.StartCalls);
        Assert.AreEqual(2002, lifecycle.WaitedProcessIds.Single());
        Assert.IsNotNull(rebound);
        Assert.AreEqual(2002, rebound.Window.ProcessId);
        Assert.AreEqual(1, lifecycle.RestoreCalls);
    }

    [TestMethod]
    public async Task Session_DoesNotRestartAfterFinalFinitePrimaryIteration()
    {
        using var fixture = new RestartFixture();
        var executor = new FakeRoundExecutor(macro =>
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(121));
            return PlaybackRunResult.Completed;
        });
        var lifecycle = fixture.CreateSuccessfulLifecycle();
        var session = new RestartSessionService(executor, lifecycle, fixture.Clock, new AdvancingDelay(fixture.Clock));

        await session.PlayAsync(fixture.Request(repeatCount: 1));

        CollectionAssert.AreEqual(new[] { fixture.Primary.Name }, executor.ExecutedMacros);
        Assert.AreEqual(0, lifecycle.CloseCalls);
        Assert.AreEqual(0, lifecycle.StartCalls);
    }

    [TestMethod]
    public async Task Session_RetriesLaunchExactlyOnceOnlyWhenTheFirstLaunchedProcessExited()
    {
        using var fixture = new RestartFixture();
        var primaryRuns = 0;
        var executor = new FakeRoundExecutor(macro =>
        {
            if (macro.Id == fixture.Primary.Id && ++primaryRuns == 1) fixture.Clock.Advance(TimeSpan.FromMinutes(2));
            return PlaybackRunResult.Completed;
        });
        var retryWindow = fixture.NewWindow(3003);
        var lifecycle = new FakeLifecycle
        {
            CloseResult = new ProcessCloseResult(true, "已关闭"),
            LaunchResults = new Queue<ProcessLaunchResult>([
                new(true, 3002, "第一次启动"),
                new(true, 3003, "第二次启动")]),
            WindowResults = new Queue<RestartWindowResult>([
                new(false, null, "第一次进程已退出", true),
                new(true, retryWindow, "已出现", false)]),
            RestoreResult = new LayoutRestoreResult(true, retryWindow, "已恢复")
        };
        var session = new RestartSessionService(executor, lifecycle, fixture.Clock, new AdvancingDelay(fixture.Clock));

        await session.PlayAsync(fixture.Request(repeatCount: 2, intervalMinutes: 1));

        Assert.AreEqual(2, lifecycle.StartCalls);
        CollectionAssert.AreEqual(new[] { 3002, 3003 }, lifecycle.WaitedProcessIds);
        CollectionAssert.AreEqual(new[] { fixture.Primary.Name, fixture.Preparation.Name, fixture.Primary.Name }, executor.ExecutedMacros);
    }

    [TestMethod]
    public async Task Session_CloseFailure_DoesNotLaunchOrRunPreparation()
    {
        using var fixture = new RestartFixture();
        var primaryRuns = 0;
        var executor = new FakeRoundExecutor(macro =>
        {
            if (macro.Id == fixture.Primary.Id && ++primaryRuns == 1) fixture.Clock.Advance(TimeSpan.FromMinutes(2));
            return PlaybackRunResult.Completed;
        });
        var lifecycle = new FakeLifecycle { CloseResult = new ProcessCloseResult(false, "正常关闭超时") };
        var session = new RestartSessionService(executor, lifecycle, fixture.Clock, new AdvancingDelay(fixture.Clock));
        PlaybackStopResult? stopped = null;
        session.PlaybackStopped += result => stopped = result;

        await session.PlayAsync(fixture.Request(repeatCount: 2, intervalMinutes: 1));

        Assert.AreEqual(1, lifecycle.CloseCalls);
        Assert.AreEqual(0, lifecycle.StartCalls);
        CollectionAssert.AreEqual(new[] { fixture.Primary.Name }, executor.ExecutedMacros);
        Assert.IsNotNull(stopped);
        Assert.AreEqual(PlaybackStopKind.RestartFailure, stopped.Kind);
        StringAssert.Contains(stopped.Reason, "正常关闭超时");
    }

    [TestMethod]
    public async Task Session_LayoutRestoreFailure_DoesNotRunPreparation()
    {
        using var fixture = new RestartFixture();
        var primaryRuns = 0;
        var executor = new FakeRoundExecutor(macro =>
        {
            if (macro.Id == fixture.Primary.Id && ++primaryRuns == 1) fixture.Clock.Advance(TimeSpan.FromMinutes(2));
            return PlaybackRunResult.Completed;
        });
        var newWindow = fixture.NewWindow();
        var lifecycle = new FakeLifecycle
        {
            CloseResult = new ProcessCloseResult(true, "已关闭"),
            LaunchResults = new Queue<ProcessLaunchResult>([new(true, newWindow.ProcessId, "已启动")]),
            WindowResults = new Queue<RestartWindowResult>([new(true, newWindow, "已出现", false)]),
            RestoreResult = new LayoutRestoreResult(false, null, "窗口布局复检失败")
        };
        var session = new RestartSessionService(executor, lifecycle, fixture.Clock, new AdvancingDelay(fixture.Clock));

        await session.PlayAsync(fixture.Request(repeatCount: 2, intervalMinutes: 1));

        CollectionAssert.AreEqual(new[] { fixture.Primary.Name }, executor.ExecutedMacros);
        Assert.AreEqual(1, lifecycle.RestoreCalls);
    }

    [TestMethod]
    public async Task Session_PreparationFailure_DoesNotResumePrimaryMacro()
    {
        using var fixture = new RestartFixture();
        var primaryRuns = 0;
        var executor = new FakeRoundExecutor(macro =>
        {
            if (macro.Id == fixture.Primary.Id && ++primaryRuns == 1) fixture.Clock.Advance(TimeSpan.FromMinutes(2));
            return macro.Id == fixture.Preparation.Id
                ? new PlaybackRunResult(false, new PlaybackStopResult(PlaybackStopKind.InputFailure, "准备宏输入失败"))
                : PlaybackRunResult.Completed;
        });
        var lifecycle = fixture.CreateSuccessfulLifecycle();
        var session = new RestartSessionService(executor, lifecycle, fixture.Clock, new AdvancingDelay(fixture.Clock));
        PlaybackStopResult? stopped = null;
        session.PlaybackStopped += result => stopped = result;

        await session.PlayAsync(fixture.Request(repeatCount: 2, intervalMinutes: 1));

        CollectionAssert.AreEqual(new[] { fixture.Primary.Name, fixture.Preparation.Name }, executor.ExecutedMacros);
        Assert.IsNotNull(stopped);
        Assert.AreEqual(PlaybackStopKind.InputFailure, stopped.Kind);
        StringAssert.Contains(stopped.Reason, "准备宏输入失败");
    }

    [TestMethod]
    public async Task Session_EmergencyStopDuringStartupBuffer_DoesNotRunPreparationOrRelaunch()
    {
        using var fixture = new RestartFixture();
        var primaryRuns = 0;
        var executor = new FakeRoundExecutor(macro =>
        {
            if (macro.Id == fixture.Primary.Id && ++primaryRuns == 1) fixture.Clock.Advance(TimeSpan.FromMinutes(2));
            return PlaybackRunResult.Completed;
        });
        var lifecycle = fixture.CreateSuccessfulLifecycle();
        var delay = new BlockingDelay();
        var session = new RestartSessionService(executor, lifecycle, fixture.Clock, delay);
        var running = session.PlayAsync(fixture.Request(repeatCount: 2, intervalMinutes: 1, startupBufferSeconds: 60));

        await delay.WaitUntilCalledAsync();
        session.EmergencyStop();
        await running.WaitAsync(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(new[] { fixture.Primary.Name }, executor.ExecutedMacros);
        Assert.AreEqual(1, lifecycle.CloseCalls);
        Assert.AreEqual(1, lifecycle.StartCalls);
        Assert.IsFalse(session.IsRunning);
    }

    [TestMethod]
    public void ValidateRequest_RejectsMissingExecutableWithoutTouchingLifecycle()
    {
        using var fixture = new RestartFixture();
        var invalid = fixture.Request() with { ExecutablePath = Path.Combine(fixture.Root, "missing.exe") };

        var error = RestartSessionService.ValidateRequest(invalid);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "EXE 路径");
    }

    [TestMethod]
    public void ValidateRequest_RejectsPreparationMacroWithIncompatibleLayout()
    {
        using var fixture = new RestartFixture();
        var incompatiblePreparation = fixture.Preparation with
        {
            DisplayLayout = fixture.Preparation.DisplayLayout! with { WindowX = fixture.Preparation.DisplayLayout.WindowX + 9 }
        };
        var invalid = fixture.Request() with { PreparationMacro = incompatiblePreparation };

        var error = RestartSessionService.ValidateRequest(invalid);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "不兼容");
    }

    [TestMethod]
    public void TargetSettings_RestartSessionRoundTripsWithSafeDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistRestartTests", Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MacroLibraryService(root);
            var preparationId = Guid.NewGuid();
            library.SaveZmboxTarget(new ZmboxTargetSettings
            {
                ExecutablePath = @"C:\\Games\\造梦盒子.exe",
                RestartSession = new RestartSessionSettings
                {
                    Enabled = true,
                    IntervalMinutes = 120,
                    StartupBufferSeconds = 60,
                    PreparationMacroId = preparationId
                }
            });

            var loaded = library.LoadZmboxTarget();

            Assert.IsTrue(loaded.RestartSession.Enabled);
            Assert.AreEqual(120, loaded.RestartSession.IntervalMinutes);
            Assert.AreEqual(60, loaded.RestartSession.StartupBufferSeconds);
            Assert.AreEqual(preparationId, loaded.RestartSession.PreparationMacroId);
            Assert.IsFalse(new RestartSessionSettings().Enabled);
            Assert.AreEqual(120, new RestartSessionSettings().IntervalMinutes);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class RestartFixture : IDisposable
    {
        public RestartFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistRestartTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ExecutablePath = Path.Combine(Root, "造梦盒子.exe");
            File.WriteAllText(ExecutablePath, "test executable placeholder");
            var layout = new DisplayLayout(1920, 1080, 96, 100, 120, 1280, 720);
            Primary = CreateMacro("主宏", layout);
            Preparation = CreateMacro("准备宏", layout);
            InitialWindow = new ManualWindowBinding(
                new WindowCandidate(new IntPtr(1001), 1001, "造梦盒子", ExecutablePath, "造梦盒子 v1.8", layout, DateTime.UtcNow),
                true);
        }

        public string Root { get; }
        public string ExecutablePath { get; }
        public FakeClock Clock { get; } = new();
        public MacroDefinition Primary { get; }
        public MacroDefinition Preparation { get; }
        public ManualWindowBinding InitialWindow { get; }

        public RestartSessionRequest Request(int repeatCount = 2, int intervalMinutes = 120, int startupBufferSeconds = 60) => new(
            Primary,
            Preparation,
            new TargetProfile
            {
                Id = ZmboxTargetSettings.TargetProfileId,
                Name = ZmboxTargetSettings.DisplayName,
                ExecutablePath = ExecutablePath,
                WindowTitleContains = "造梦盒子",
                BackgroundCapability = BackgroundCapability.Verified
            },
            new PlaybackOptions(repeatCount, false, 1, PlaybackMode.ForegroundSystemInput, 0, 10, 0),
            InitialWindow,
            new RestartSessionSettings
            {
                Enabled = true,
                IntervalMinutes = intervalMinutes,
                StartupBufferSeconds = startupBufferSeconds,
                PreparationMacroId = Preparation.Id
            },
            ExecutablePath);

        public WindowCandidate NewWindow(int processId = 2001) => new(
            new IntPtr(processId),
            processId,
            "造梦盒子",
            ExecutablePath,
            "造梦盒子 v1.8",
            InitialWindow.Window.Layout,
            DateTime.UtcNow.AddMinutes(1));

        public FakeLifecycle CreateSuccessfulLifecycle(int newProcessId = 2001)
        {
            var window = NewWindow(newProcessId);
            return new FakeLifecycle
            {
                CloseResult = new ProcessCloseResult(true, "已关闭"),
                LaunchResults = new Queue<ProcessLaunchResult>([new(true, newProcessId, "已启动")]),
                WindowResults = new Queue<RestartWindowResult>([new(true, window, "已出现", false)]),
                RestoreResult = new LayoutRestoreResult(true, window, "已恢复")
            };
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        private static MacroDefinition CreateMacro(string name, DisplayLayout layout) => new()
        {
            Name = name,
            DisplayLayout = layout,
            Events =
            [
                new RecordedEvent { OffsetMicroseconds = 0, Kind = InputEventKind.KeyDown, VirtualKey = 0x57 },
                new RecordedEvent { OffsetMicroseconds = 100_000, Kind = InputEventKind.KeyUp, VirtualKey = 0x57 }
            ]
        };
    }

    private sealed class FakeRoundExecutor : IPlaybackRoundExecutor
    {
        private readonly Func<MacroDefinition, PlaybackRunResult> _run;

        public FakeRoundExecutor(Func<MacroDefinition, PlaybackRunResult> run) => _run = run;
        public List<string> ExecutedMacros { get; } = [];
        public int EmergencyStopCalls { get; private set; }
        public bool IsRunning => false;

        public Task<PlaybackRunResult> PlaySingleIterationAsync(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding manualWindow, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutedMacros.Add(macro.Name);
            return Task.FromResult(_run(macro));
        }

        public void EmergencyStop() => EmergencyStopCalls++;
    }

    private sealed class FakeLifecycle : IZmboxProcessLifecycle
    {
        public ProcessCloseResult CloseResult { get; init; } = new(true, "已关闭");
        public Queue<ProcessLaunchResult> LaunchResults { get; init; } = [];
        public Queue<RestartWindowResult> WindowResults { get; init; } = [];
        public LayoutRestoreResult RestoreResult { get; init; } = new(false, null, "未配置恢复结果");
        public int CloseCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int ClosedProcessId { get; private set; }
        public List<int> WaitedProcessIds { get; } = [];

        public Task<ProcessCloseResult> RequestNormalCloseAsync(ManualWindowBinding lockedWindow, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCalls++;
            ClosedProcessId = lockedWindow.Window.ProcessId;
            return Task.FromResult(CloseResult);
        }

        public Task<ProcessLaunchResult> StartAsync(string executablePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCalls++;
            return Task.FromResult(LaunchResults.Count > 0 ? LaunchResults.Dequeue() : new ProcessLaunchResult(false, 0, "没有可用启动结果"));
        }

        public Task<RestartWindowResult> WaitForWindowAsync(ProcessLaunchResult launchedProcess, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitedProcessIds.Add(launchedProcess.ProcessId);
            return Task.FromResult(WindowResults.Count > 0 ? WindowResults.Dequeue() : new RestartWindowResult(false, null, "没有可用窗口结果", false));
        }

        public LayoutRestoreResult RestoreLayout(WindowCandidate window, DisplayLayout expectedLayout)
        {
            RestoreCalls++;
            return RestoreResult;
        }

        public bool TryActivate(WindowCandidate window) => true;
    }

    private sealed class FakeClock : IRestartSessionClock
    {
        private long _ticks;
        public long GetTimestamp() => _ticks;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.FromTicks(endTimestamp - startTimestamp);
        public void Advance(TimeSpan span) => _ticks += span.Ticks;
    }

    private sealed class AdvancingDelay : IRestartSessionDelay
    {
        private readonly FakeClock _clock;
        public AdvancingDelay(FakeClock clock) => _clock = clock;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _clock.Advance(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDelay : IRestartSessionDelay
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _called.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task WaitUntilCalledAsync() => _called.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
