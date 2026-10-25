using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class MacroSchemaV2Tests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));

    private static MacroDefinition NewMacro() => new()
    {
        Name = "compact",
        TargetProfileId = ZmboxTargetSettings.TargetProfileId,
        DisplayLayout = new DisplayLayout(1920, 1080, 96, 100, 100, 1284, 1158),
        Events =
        [
            new RecordedEvent { OffsetMicroseconds = 0, Kind = InputEventKind.KeyDown, VirtualKey = 0x57 },
            new RecordedEvent { OffsetMicroseconds = 141_365, Kind = InputEventKind.MouseMove, X = 1644, Y = 1157 },
            new RecordedEvent { OffsetMicroseconds = 200_000, Kind = InputEventKind.MouseDown, Button = MouseButtonKind.X1, X = 1644, Y = 1157 },
            new RecordedEvent { OffsetMicroseconds = 260_000, Kind = InputEventKind.MouseWheel, X = 800, Y = 600, WheelDelta = -120 },
            new RecordedEvent { OffsetMicroseconds = 300_000, Kind = InputEventKind.KeyUp, VirtualKey = 0x57 }
        ]
    };

    [TestMethod]
    public void SaveMacro_WritesCompactArraysAndCurrentVersion()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            library.SaveMacro(NewMacro());

            var text = File.ReadAllText(Directory.EnumerateFiles(library.MacrosDirectory, "*.json").Single());
            StringAssert.Contains(text, $"\"SchemaVersion\": {MacroDefinition.CurrentSchemaVersion}");
            StringAssert.Contains(text, "\"Events\": [");
            Assert.IsFalse(text.Contains("OffsetMicroseconds"), "紧凑格式不应再写属性名。");
            StringAssert.Contains(text, "[141365,2,0,0,1644,1157,0]");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SaveThenLoad_PreservesEveryEventField()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var original = NewMacro();
            library.SaveMacro(original);

            var loaded = library.LoadMacros().Single();
            CollectionAssert.AreEqual(original.Events.ToArray(), loaded.Events.ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LoadMacros_StillReadsLegacyObjectEvents()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var legacy = """
                {
                  "SchemaVersion": 1,
                  "Id": "c5079121-4f52-4f46-9202-75f6f9c3ce04",
                  "Name": "old object form",
                  "Events": [
                    { "OffsetMicroseconds": 0, "Kind": "KeyDown", "VirtualKey": 87 },
                    { "OffsetMicroseconds": 1000, "Kind": 2, "X": 10, "Y": 20 },
                    { "OffsetMicroseconds": 2000, "Kind": "KeyUp", "VirtualKey": 87 }
                  ]
                }
                """;
            File.WriteAllText(Path.Combine(library.MacrosDirectory, "legacy.json"), legacy);

            var result = library.LoadMacrosWithIssues();

            Assert.AreEqual(0, result.Issues.Count);
            var macro = result.Macros.Single();
            Assert.AreEqual(InputEventKind.MouseMove, macro.Events[1].Kind);
            Assert.AreEqual(20, macro.Events[1].Y);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LegacyMacroIsUpgradedWhenSaved()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var macro = NewMacro();
            File.WriteAllText(
                Path.Combine(library.MacrosDirectory, $"{macro.Id:N}.json"),
                $$"""
                { "SchemaVersion": 1, "Id": "{{macro.Id}}", "Name": "legacy", "Events": [ { "Kind": "KeyDown", "VirtualKey": 87 } ] }
                """);

            var loaded = library.LoadMacros().Single();
            library.SaveMacro(loaded);

            var text = File.ReadAllText(Path.Combine(library.MacrosDirectory, $"{macro.Id:N}.json"));
            StringAssert.Contains(text, $"\"SchemaVersion\": {MacroDefinition.CurrentSchemaVersion}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void MalformedCompactRowIsReportedNotFatal()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var id = Guid.NewGuid();
            File.WriteAllText(
                Path.Combine(library.MacrosDirectory, $"{id:N}.json"),
                $$"""
                { "SchemaVersion": 2, "Id": "{{id}}", "Name": "broken", "Events": [ [123] ] }
                """);

            var result = library.LoadMacrosWithIssues();

            Assert.AreEqual(0, result.Macros.Count);
            Assert.AreEqual(1, result.Issues.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
