using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class MacroValidationAtLoadTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));

    [TestMethod]
    public void LoadMacros_RejectsUnsortedTimelineAsAnIssue()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var id = Guid.NewGuid();
            File.WriteAllText(
                Path.Combine(library.MacrosDirectory, $"{id:N}.json"),
                $$"""
                { "SchemaVersion": 2, "Id": "{{id}}", "Name": "unsorted", "Events": [ [2000,0,87,0,0,0,0], [1000,1,87,0,0,0,0] ] }
                """);

            var result = library.LoadMacrosWithIssues();

            Assert.AreEqual(0, result.Macros.Count);
            Assert.AreEqual(1, result.Issues.Count);
            StringAssert.Contains(result.Issues[0].Message, "未按时间排序");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LoadMacros_RejectsEmptyMacroAsAnIssue()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var id = Guid.NewGuid();
            File.WriteAllText(
                Path.Combine(library.MacrosDirectory, $"{id:N}.json"),
                $$"""
                { "SchemaVersion": 2, "Id": "{{id}}", "Name": "empty", "Events": [] }
                """);

            var result = library.LoadMacrosWithIssues();

            Assert.AreEqual(0, result.Macros.Count);
            Assert.AreEqual(1, result.Issues.Count);
            StringAssert.Contains(result.Issues[0].Message, "没有可回放的输入事件");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SaveMacro_RejectsUnsortedTimeline()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var macro = new MacroDefinition
            {
                Name = "unsorted",
                Events =
                [
                    new RecordedEvent { OffsetMicroseconds = 500, Kind = InputEventKind.KeyDown, VirtualKey = 87 },
                    new RecordedEvent { OffsetMicroseconds = 100, Kind = InputEventKind.KeyUp, VirtualKey = 87 }
                ]
            };

            var error = Assert.ThrowsException<InvalidOperationException>(() => library.SaveMacro(macro));
            StringAssert.Contains(error.Message, "未按时间排序");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SaveMacro_RejectsEmptyRecording()
    {
        var root = NewRoot();
        try
        {
            var library = new MacroLibraryService(root);
            var macro = new MacroDefinition { Name = "empty" };

            var error = Assert.ThrowsException<InvalidOperationException>(() => library.SaveMacro(macro));
            StringAssert.Contains(error.Message, "没有可回放的输入事件");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
