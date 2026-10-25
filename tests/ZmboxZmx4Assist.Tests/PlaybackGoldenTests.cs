using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Tests;

/// <summary>
/// Golden baseline for the playback dispatch sequence. Storage/format refactors must not change
/// what the timeline hands to the input seam for a given macro, so these tests pin it down.
/// </summary>
[TestClass]
public sealed class PlaybackGoldenTests
{
    private static readonly DisplayLayout Layout = new(1920, 1080, 96, 0, 0, 1920, 1080);

    [TestMethod]
    public async Task Replay_DispatchesRecordedEventsInOrderThroughTheForegroundChannel()
    {
        var macro = BuildMacro();
        var sink = new RecordingInputSink();
        var player = new PlaybackService(new TargetWindowService(), sink, new AlwaysReadyGuard());

        await player.PlayAsync(macro, BuildProfile(), PlaybackOptions.Default);

        Assert.AreEqual(6, sink.Dispatched.Count, string.Join(" ; ", sink.Dispatched));

        CollectionAssert.AreEqual(
            new[]
            {
                "ForegroundSystemInput|KeyDown|87|None|0|0|0",
                "ForegroundSystemInput|MouseMove|0|None|960|540|0",
                "ForegroundSystemInput|MouseDown|0|Left|960|540|0",
                "ForegroundSystemInput|MouseUp|0|Left|960|540|0",
                "ForegroundSystemInput|MouseWheel|0|None|960|540|120",
                "ForegroundSystemInput|KeyUp|87|None|0|0|0"
            },
            sink.Dispatched.ToArray());
        Assert.AreEqual(0, sink.Released.Count);
    }

    [TestMethod]
    public async Task Replay_ReleasesAHeldKeyWhenTheMacroEndsWhileItIsStillDown()
    {
        var macro = BuildMacro(includeKeyUp: false);
        var sink = new RecordingInputSink();
        var player = new PlaybackService(new TargetWindowService(), sink, new AlwaysReadyGuard());

        await player.PlayAsync(macro, BuildProfile(), PlaybackOptions.Default);

        Assert.AreEqual("ForegroundSystemInput|KeyUp|87|None", sink.Released.Single());
    }

    [TestMethod]
    public async Task Replay_BackgroundModeUsesTheBackgroundChannelForEveryEvent()
    {
        var macro = BuildMacro();
        var sink = new RecordingInputSink();
        var player = new PlaybackService(new TargetWindowService(), sink, new AlwaysReadyGuard());

        await player.PlayAsync(
            macro,
            BuildProfile(),
            PlaybackOptions.Default with { Mode = PlaybackMode.ExperimentalTargetWindow });

        Assert.AreEqual(6, sink.Dispatched.Count);
        Assert.IsTrue(sink.Dispatched.All(op => op.StartsWith("ExperimentalTargetWindow|", StringComparison.Ordinal)));
    }

    private static MacroDefinition BuildMacro(bool includeKeyUp = true)
    {
        var events = new List<RecordedEvent>
        {
            new() { OffsetMicroseconds = 0, Kind = InputEventKind.KeyDown, VirtualKey = 0x57 },
            new() { OffsetMicroseconds = 1_000, Kind = InputEventKind.MouseMove, X = 960, Y = 540 },
            new() { OffsetMicroseconds = 2_000, Kind = InputEventKind.MouseDown, Button = MouseButtonKind.Left, X = 960, Y = 540 },
            new() { OffsetMicroseconds = 3_000, Kind = InputEventKind.MouseUp, Button = MouseButtonKind.Left, X = 960, Y = 540 },
            new() { OffsetMicroseconds = 4_000, Kind = InputEventKind.MouseWheel, X = 960, Y = 540, WheelDelta = 120 }
        };
        if (includeKeyUp) events.Add(new RecordedEvent { OffsetMicroseconds = 5_000, Kind = InputEventKind.KeyUp, VirtualKey = 0x57 });
        return new MacroDefinition
        {
            Name = "golden",
            TargetProfileId = ZmboxTargetSettings.TargetProfileId,
            DisplayLayout = Layout,
            Events = events
        };
    }

    private static TargetProfile BuildProfile() => new()
    {
        Id = ZmboxTargetSettings.TargetProfileId,
        Name = ZmboxTargetSettings.DisplayName,
        WindowTitleContains = "造梦盒子",
        BackgroundCapability = BackgroundCapability.Verified
    };

    private sealed class RecordingInputSink : IInputSink
    {
        public List<string> Dispatched { get; } = [];

        public List<string> Released { get; } = [];

        public void Dispatch(IntPtr window, RecordedEvent e, PlaybackMode mode, VirtualScreenBounds bounds) =>
            Dispatched.Add($"{mode}|{e.Kind}|{e.VirtualKey}|{e.Button}|{e.X}|{e.Y}|{e.WheelDelta}");

        public void Release(IntPtr window, RecordedEvent e, PlaybackMode mode) =>
            Released.Add($"{mode}|{e.Kind}|{e.VirtualKey}|{e.Button}");
    }

    private sealed class AlwaysReadyGuard : IPlaybackTargetGuard
    {
        private static readonly IntPtr Window = new(0x1234);

        public TargetReadiness Find(MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow) =>
            new(Window, true, string.Empty);

        public TargetReadiness Check(IntPtr window, MacroDefinition macro, TargetProfile profile, PlaybackOptions options, ManualWindowBinding? manualWindow) =>
            new(window, true, string.Empty);
    }
}
