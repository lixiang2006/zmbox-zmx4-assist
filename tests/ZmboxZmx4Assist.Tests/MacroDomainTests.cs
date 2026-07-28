using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;
using ZmboxZmx4Assist.Utilities;
using System.Text.Json;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class MacroDomainTests
{
    [TestMethod]
    public void Compare_UnavailableWindowBounds_ReportsTransientWindowState()
    {
        var expected = new DisplayLayout(1920, 1080, 96, 100, 100, 1284, 1158);
        var unavailable = new DisplayLayout(1920, 1080, 96, -64_384, -64_080, 0, 0);

        var actual = DisplayLayoutComparer.Compare(expected, unavailable);

        Assert.IsFalse(actual.IsMatch);
        Assert.IsTrue(actual.IsTransient);
        StringAssert.Contains(actual.Reason, "暂时没有可用布局");
    }

    [TestMethod]
    public void Normalize_TinyMotionDuringShortClick_RemovesIntermediateMouseMove()
    {
        var events = new[]
        {
            new RecordedEvent { OffsetMicroseconds = 0, Kind = InputEventKind.MouseDown, Button = MouseButtonKind.Left, X = 100, Y = 100 },
            new RecordedEvent { OffsetMicroseconds = 50_000, Kind = InputEventKind.MouseMove, X = 103, Y = 102 },
            new RecordedEvent { OffsetMicroseconds = 100_000, Kind = InputEventKind.MouseUp, Button = MouseButtonKind.Left, X = 103, Y = 102 }
        };

        var actual = MouseGestureProcessor.Normalize(events);
        Assert.AreEqual(2, actual.Count);
        Assert.AreEqual(InputEventKind.MouseDown, actual[0].Kind);
        Assert.AreEqual(InputEventKind.MouseUp, actual[1].Kind);
    }

    [TestMethod]
    public void Normalize_IntentionalDrag_PreservesMouseMove()
    {
        var events = new[]
        {
            new RecordedEvent { OffsetMicroseconds = 0, Kind = InputEventKind.MouseDown, Button = MouseButtonKind.Left, X = 20, Y = 20 },
            new RecordedEvent { OffsetMicroseconds = 150_000, Kind = InputEventKind.MouseMove, X = 60, Y = 20 },
            new RecordedEvent { OffsetMicroseconds = 400_000, Kind = InputEventKind.MouseUp, Button = MouseButtonKind.Left, X = 60, Y = 20 }
        };

        var actual = MouseGestureProcessor.Normalize(events);
        Assert.AreEqual(3, actual.Count);
        Assert.IsTrue(actual.Any(x => x.Kind == InputEventKind.MouseMove));
    }

    [TestMethod]
    public void Validate_UnorderedTimeline_ReturnsError()
    {
        var macro = new MacroDefinition { Events = new[] { new RecordedEvent { OffsetMicroseconds = 100, Kind = InputEventKind.KeyDown }, new RecordedEvent { OffsetMicroseconds = 0, Kind = InputEventKind.KeyUp } } };
        StringAssert.Contains(MacroValidator.Validate(macro)!, "未按时间排序");
    }

    [TestMethod]
    public void Library_RoundTripsMacroAndProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MacroLibraryService(root);
            var profile = new TargetProfile { Name = "test profile" };
            library.SaveProfiles(new[] { profile });
            var macro = new MacroDefinition { Name = "hold W", TargetProfileId = profile.Id, Events = new[] { new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = 0x57 }, new RecordedEvent { OffsetMicroseconds = 2_000_000, Kind = InputEventKind.KeyUp, VirtualKey = 0x57 } } };
            library.SaveMacro(macro);

            Assert.AreEqual("test profile", library.LoadProfiles().Single().Name);
            Assert.AreEqual(2, library.LoadMacros().Single().Events.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Library_ReplacesLastRetiredLegacyHallPresetWithNeutralProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MacroLibraryService(root);
            library.SaveProfiles(new[] { new TargetProfile { Name = "360 造梦西游4", BackgroundCapability = BackgroundCapability.Unknown } });

            var profile = library.LoadProfiles().Single();
            Assert.AreEqual("新建启动器", profile.Name);
            Assert.AreEqual(BackgroundCapability.Unknown, profile.BackgroundCapability);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Library_RemovesTheRetiredLegacyHallPresetButPreservesOtherProfiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MacroLibraryService(root);
            library.SaveProfiles(new[]
            {
                new TargetProfile { Name = "360 造梦西游4", ExecutablePath = "C:\\legacy\\360Game.exe" },
                new TargetProfile { Name = "造梦盒子", ExecutablePath = "C:\\launcher\\zmBox.exe", WindowTitleContains = "造梦盒子" }
            });

            var profiles = library.LoadProfiles();

            Assert.AreEqual(1, profiles.Count);
            Assert.AreEqual("造梦盒子", profiles.Single().Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Library_ImportsLegacyMacrosAndZmboxSettingsWithoutChangingLegacyFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistLegacyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var old = new MacroLibraryService(legacy);
            old.SaveMacro(new MacroDefinition { Name = "旧宏", Events = new[] { new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = 0x57 } } });
            old.SaveProfiles(new[] { new TargetProfile { Name = "造梦盒子", ExecutablePath = @"C:\\Games\\造梦盒子.exe", WindowTitleContains = "造梦盒子v1.8" } });
            var legacyMacro = Directory.EnumerateFiles(old.MacrosDirectory, "*.json").Single();
            var before = File.ReadAllText(legacyMacro);

            var migrated = new MacroLibraryService(root, legacy);

            Assert.AreEqual("旧宏", migrated.LoadMacros().Single().Name);
            Assert.AreEqual(@"C:\\Games\\造梦盒子.exe", migrated.LoadZmboxTarget().ExecutablePath);
            Assert.AreEqual(before, File.ReadAllText(legacyMacro));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(legacy)) Directory.Delete(legacy, true);
        }
    }

    [TestMethod]
    public void ZmboxMatcher_RequiresBothLauncherProcessAndTitle()
    {
        var targets = new TargetWindowService();
        var layout = new DisplayLayout(1920, 1080, 96, 0, 0, 800, 600);

        Assert.IsTrue(targets.IsZmboxWindow(new WindowCandidate(IntPtr.Zero, 1, "造梦盒子", string.Empty, "造梦盒子v1.8 - By Duskeye", layout)));
        Assert.IsFalse(targets.IsZmboxWindow(new WindowCandidate(IntPtr.Zero, 1, "not-zmbox", string.Empty, "造梦盒子v1.8", layout)));
        Assert.IsFalse(targets.IsZmboxWindow(new WindowCandidate(IntPtr.Zero, 1, "造梦盒子", string.Empty, "其他窗口", layout)));
    }

    [TestMethod]
    public void Library_RenameMacro_PreservesIdAndEventsWithoutTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MacroLibraryService(root);
            var macro = new MacroDefinition { Name = "旧名称", Events = new[] { new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = 0x57 } } };
            library.SaveMacro(macro);

            var renamed = library.RenameMacro(macro, "  新名称  ");
            var loaded = library.LoadMacros().Single();

            Assert.AreEqual(macro.Id, renamed.Id);
            Assert.AreEqual(macro.Id, loaded.Id);
            Assert.AreEqual("新名称", loaded.Name);
            Assert.AreEqual(1, loaded.Events.Count);
            Assert.IsFalse(Directory.EnumerateFiles(library.MacrosDirectory, "*.tmp").Any());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Library_LoadMacrosWithIssues_LeavesBadFileAndLoadsHealthyMacro()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MacroLibraryService(root);
            library.SaveMacro(new MacroDefinition { Name = "正常宏", Events = new[] { new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = 0x57 } } });
            var badPath = Path.Combine(library.MacrosDirectory, "损坏宏.json");
            File.WriteAllText(badPath, "{ this is not json }");

            var result = library.LoadMacrosWithIssues();

            Assert.AreEqual(1, result.Macros.Count);
            Assert.AreEqual(1, result.Issues.Count);
            Assert.AreEqual("损坏宏.json", result.Issues.Single().FileName);
            Assert.IsTrue(File.Exists(badPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void MacroNameValidation_RejectsBlankAndOverlongNames()
    {
        Assert.IsNotNull(MacroLibraryService.ValidateMacroName("  "));
        Assert.IsNotNull(MacroLibraryService.ValidateMacroName(new string('a', 81)));
        Assert.IsNull(MacroLibraryService.ValidateMacroName("刷图宏 01"));
    }

    [TestMethod]
    public void ConfiguredHotkeys_AreRecognizedAsControlKeys()
    {
        var settings = new HotkeySettings
        {
            RecordHotkey = new HotkeyBinding(0x70, HotkeyModifiers.Control),
            PlayHotkey = new HotkeyBinding(0x71, HotkeyModifiers.Alt),
            EmergencyHotkey = new HotkeyBinding(0x7B, HotkeyModifiers.Shift)
        };

        Assert.IsTrue(settings.IsControlKey(0x70));
        Assert.IsTrue(settings.IsControlKey(0x71));
        Assert.IsTrue(settings.IsControlKey(0x7B));
        Assert.IsTrue(settings.IsControlKey(0x11));
        Assert.IsTrue(settings.IsControlKey(0x12));
        Assert.IsTrue(settings.IsControlKey(0x10));
        Assert.IsFalse(settings.IsControlKey(0x77));
    }

    [TestMethod]
    public void Hotkeys_LegacySingleKeyJson_LoadsAsUnmodifiedBindings()
    {
        const string json = "{\"RecordHotkey\":119,\"PlayHotkey\":120,\"EmergencyHotkey\":123}";

        var settings = JsonSerializer.Deserialize<HotkeySettings>(json)!;

        Assert.AreEqual(new HotkeyBinding(0x77), settings.RecordHotkey);
        Assert.AreEqual(new HotkeyBinding(0x78), settings.PlayHotkey);
        Assert.AreEqual(new HotkeyBinding(0x7B), settings.EmergencyHotkey);
    }

    [TestMethod]
    public void Hotkeys_CombinationRoundTripsAndDuplicateBindingsAreRejected()
    {
        var settings = new HotkeySettings
        {
            RecordHotkey = new HotkeyBinding(0x77, HotkeyModifiers.Control | HotkeyModifiers.Alt),
            PlayHotkey = new HotkeyBinding(0x78, HotkeyModifiers.Shift),
            EmergencyHotkey = new HotkeyBinding(0x7B, HotkeyModifiers.Windows)
        };

        var roundTrip = JsonSerializer.Deserialize<HotkeySettings>(JsonSerializer.Serialize(settings))!;
        Assert.AreEqual(settings, roundTrip);
        Assert.AreEqual("Ctrl + Alt + F8", roundTrip.RecordHotkey.DisplayText);

        var duplicate = settings with { PlayHotkey = settings.RecordHotkey };
        StringAssert.Contains(duplicate.Validate()!, "不能重复");
    }

    [TestMethod]
    public void PlaybackOptions_CooldownOnlyRunsAtConfiguredBoundaryWhenAnotherRoundRemains()
    {
        var options = PlaybackOptions.Default;

        Assert.IsFalse(options.ShouldCooldownAfter(9, true));
        Assert.IsTrue(options.ShouldCooldownAfter(10, true));
        Assert.IsTrue(options.ShouldCooldownAfter(20, true));
        Assert.IsFalse(options.ShouldCooldownAfter(10, false));
        Assert.IsFalse((options with { CooldownSeconds = 0 }).ShouldCooldownAfter(10, true));
    }

    [TestMethod]
    public void LayoutComparison_AllowsEightPhysicalPixelsButRejectsNine()
    {
        var expected = new DisplayLayout(1920, 1080, 96, 100, 200, 1280, 720);
        var withinTolerance = new DisplayLayout(1920, 1080, 96, 108, 192, 1288, 712);
        var outsideTolerance = withinTolerance with { WindowX = 109 };

        Assert.IsTrue(DisplayLayoutComparer.Compare(expected, withinTolerance).IsMatch);
        var comparison = DisplayLayoutComparer.Compare(expected, outsideTolerance);
        Assert.IsFalse(comparison.IsMatch);
        Assert.AreEqual(9, comparison.XDifference);
        StringAssert.Contains(comparison.Reason, "±8px");
    }

    [TestMethod]
    public void LayoutComparison_AlwaysRejectsDisplayOrDpiChanges()
    {
        var expected = new DisplayLayout(1920, 1080, 96, 100, 200, 1280, 720);

        var resolutionChanged = DisplayLayoutComparer.Compare(expected, expected with { Width = 2560 });
        var dpiChanged = DisplayLayoutComparer.Compare(expected, expected with { Dpi = 120 });

        Assert.IsFalse(resolutionChanged.IsMatch);
        StringAssert.Contains(resolutionChanged.Reason, "分辨率");
        Assert.IsFalse(dpiChanged.IsMatch);
        StringAssert.Contains(dpiChanged.Reason, "DPI");
    }

    [TestMethod]
    public void XButtonTranslator_MapsBothSideButtons()
    {
        Assert.AreEqual(MouseButtonKind.X1, LowLevelMouseTranslator.XButtonFromMouseData(1u << 16));
        Assert.AreEqual(MouseButtonKind.X2, LowLevelMouseTranslator.XButtonFromMouseData(2u << 16));
        Assert.AreEqual(MouseButtonKind.None, LowLevelMouseTranslator.XButtonFromMouseData(3u << 16));
    }
}
