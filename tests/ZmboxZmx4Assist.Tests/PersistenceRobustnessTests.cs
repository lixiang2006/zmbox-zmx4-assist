using System.Text.Json;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Services;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class PersistenceRobustnessTests
{
    [TestMethod]
    public void HotkeyConverter_StringVirtualKey_ThrowsJsonException()
    {
        const string json = "{\"VirtualKey\":\"F8\",\"Modifiers\":0}";

        Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<HotkeyBinding>(json));
    }

    [TestMethod]
    public void HotkeyConverter_NegativeVirtualKey_ThrowsJsonException()
    {
        Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<HotkeyBinding>("-1"));
    }

    [TestMethod]
    public void HotkeyConverter_UnknownModifierName_ThrowsJsonException()
    {
        const string json = "{\"VirtualKey\":119,\"Modifiers\":\"NotAModifier\"}";

        Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<HotkeyBinding>(json));
    }

    [TestMethod]
    public void HotkeyConverter_StringTokenInsteadOfObject_ThrowsJsonException()
    {
        Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<HotkeyBinding>("\"F8\""));
    }

    [TestMethod]
    public void Library_LoadHotkeys_CorruptSettingsFileFallsBackToDefaultsAndKeepsFile()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new MacroLibraryService(root);
            var path = Path.Combine(root, "settings.json");
            const string corrupt = "{\"RecordHotkey\":{\"VirtualKey\":\"F8\"}}";
            File.WriteAllText(path, corrupt);

            var hotkeys = service.LoadHotkeys();

            Assert.AreEqual(new HotkeySettings(), hotkeys);
            Assert.AreEqual(corrupt, File.ReadAllText(path));
        }
        finally { DeleteTempRoot(root); }
    }

    [TestMethod]
    public void Library_LoadProfiles_CorruptFileFallsBackToSingleDefaultWithoutOverwriting()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new MacroLibraryService(root);
            var path = Path.Combine(root, "profiles.json");
            const string corrupt = "{ this is not json }";
            File.WriteAllText(path, corrupt);

            var profiles = service.LoadProfiles();

            Assert.AreEqual(1, profiles.Count);
            Assert.AreEqual(new TargetProfile().Name, profiles[0].Name);
            Assert.AreEqual(corrupt, File.ReadAllText(path));
        }
        finally { DeleteTempRoot(root); }
    }

    [TestMethod]
    public void Library_LoadMacrosWithIssues_SkipsMacroWrittenByNewerSchemaVersion()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new MacroLibraryService(root);
            var future = new MacroDefinition
            {
                Name = "未来版本宏",
                SchemaVersion = MacroDefinition.CurrentSchemaVersion + 1,
                Events = new[] { new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = 0x57 } }
            };
            service.SaveMacro(future);

            var result = service.LoadMacrosWithIssues();

            Assert.AreEqual(0, result.Macros.Count);
            Assert.AreEqual(1, result.Issues.Count);
            Assert.AreEqual($"{future.Id:N}.json", result.Issues[0].FileName);
            StringAssert.Contains(result.Issues[0].Message, "版本");
        }
        finally { DeleteTempRoot(root); }
    }

    [TestMethod]
    public void Library_LoadMacrosWithIssues_StillLoadsCurrentSchemaVersion()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new MacroLibraryService(root);
            service.SaveMacro(new MacroDefinition
            {
                Name = "当前版本宏",
                Events = new[] { new RecordedEvent { Kind = InputEventKind.KeyDown, VirtualKey = 0x57 } }
            });

            var result = service.LoadMacrosWithIssues();

            Assert.AreEqual(1, result.Macros.Count);
            Assert.AreEqual(0, result.Issues.Count);
        }
        finally { DeleteTempRoot(root); }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZmboxZmx4AssistTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
