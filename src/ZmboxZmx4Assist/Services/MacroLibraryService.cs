using System.Text.Json;
using ZmboxZmx4Assist.Domain;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Services;

public sealed class MacroLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new RecordedEventsJsonConverter() }
    };
    private readonly string _root;
    private readonly string _macros;
    private readonly string _settingsPath;
    private readonly string _targetPath;
    private readonly string? _legacyRoot;

    public MacroLibraryService(string? root = null, string? legacyRoot = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _root = root ?? Path.Combine(local, "ZmboxZmx4Assist");
        _legacyRoot = legacyRoot ?? (root is null ? Path.Combine(local, "GameMacro") : null);
        _macros = Path.Combine(_root, "macros");
        _settingsPath = Path.Combine(_root, "settings.json");
        _targetPath = Path.Combine(_root, "target.json");
        Directory.CreateDirectory(_macros);
        ImportLegacyDataOnce();
    }

    public string MacrosDirectory => _macros;

    public ZmboxTargetSettings LoadZmboxTarget()
    {
        if (!File.Exists(_targetPath)) return new ZmboxTargetSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<ZmboxTargetSettings>(File.ReadAllText(_targetPath), JsonOptions) ?? new ZmboxTargetSettings();
            // Older or manually edited local JSON can omit/null the new block.
            // Normalize it in memory without overwriting the user's source file.
            return settings with { RestartSession = settings.RestartSession ?? new RestartSessionSettings() };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new ZmboxTargetSettings();
        }
    }

    public void SaveZmboxTarget(ZmboxTargetSettings settings) => WriteJsonAtomically(_targetPath, settings);

    public HotkeySettings LoadHotkeys()
    {
        if (!File.Exists(_settingsPath)) return new HotkeySettings();
        try
        {
            var settings = JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new HotkeySettings();
            return settings.Validate() is null ? settings : new HotkeySettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new HotkeySettings();
        }
    }

    public void SaveHotkeys(HotkeySettings hotkeys) => WriteJsonAtomically(_settingsPath, hotkeys);

    public IReadOnlyList<MacroDefinition> LoadMacros() => LoadMacrosWithIssues().Macros;

    public MacroLoadResult LoadMacrosWithIssues()
    {
        var macros = new List<MacroDefinition>();
        var issues = new List<MacroLoadIssue>();
        foreach (var path in Directory.EnumerateFiles(_macros, "*.json"))
        {
            try
            {
                var macro = JsonSerializer.Deserialize<MacroDefinition>(File.ReadAllText(path), JsonOptions);
                if (macro is null) throw new JsonException("文件没有包含可用宏数据。");
                if (macro.SchemaVersion > MacroDefinition.CurrentSchemaVersion)
                {
                    throw new JsonException(
                        $"宏格式版本 {macro.SchemaVersion} 高于本程序支持的 {MacroDefinition.CurrentSchemaVersion}；已跳过，避免回放行为不一致。");
                }
                // Timeline validity is checked here, once per file, instead of on every playback
                // iteration; a damaged file is reported and skipped like any other load failure.
                if (MacroValidator.Validate(macro) is { } validationError) throw new JsonException($"宏数据无效：{validationError}");
                // Upgrade older files in memory: the next save then writes the compact current form.
                // Macros from the retired multi-profile builds are normalized to the single target
                // so the playback path never has to ask which profile a macro belongs to.
                macros.Add(macro with
                {
                    SchemaVersion = MacroDefinition.CurrentSchemaVersion,
                    TargetProfileId = ZmboxTargetSettings.TargetProfileId
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                issues.Add(new MacroLoadIssue(Path.GetFileName(path), ex.Message));
            }
        }
        return new MacroLoadResult(macros.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), issues);
    }

    public void SaveMacro(MacroDefinition macro)
    {
        var error = ValidateMacroName(macro.Name);
        if (error is not null) throw new InvalidOperationException(error);
        if (MacroValidator.Validate(macro) is { } validationError) throw new InvalidOperationException(validationError);
        var path = Path.Combine(_macros, $"{macro.Id:N}.json");
        WriteJsonAtomically(path, macro);
    }

    public MacroDefinition RenameMacro(MacroDefinition macro, string name)
    {
        var normalized = name.Trim();
        var error = ValidateMacroName(normalized);
        if (error is not null) throw new InvalidOperationException(error);
        var renamed = macro with { Name = normalized };
        SaveMacro(renamed);
        return renamed;
    }

    public void DeleteMacro(MacroDefinition macro)
    {
        var path = Path.Combine(_macros, $"{macro.Id:N}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public static string? ValidateMacroName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "宏名称不能为空。";
        if (name.Trim().Length > 80) return "宏名称不能超过 80 个字符。";
        if (name.Any(char.IsControl)) return "宏名称不能包含控制字符。";
        return null;
    }

    private void WriteJsonAtomically<T>(string path, T value)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void ImportLegacyDataOnce()
    {
        if (string.IsNullOrWhiteSpace(_legacyRoot) || !Directory.Exists(_legacyRoot)) return;
        var legacyMacros = Path.Combine(_legacyRoot, "macros");
        if (Directory.Exists(legacyMacros) && !Directory.EnumerateFiles(_macros, "*.json").Any())
        {
            foreach (var path in Directory.EnumerateFiles(legacyMacros, "*.json"))
                File.Copy(path, Path.Combine(_macros, Path.GetFileName(path)), false);
        }
        var legacySettings = Path.Combine(_legacyRoot, "settings.json");
        if (File.Exists(legacySettings) && !File.Exists(_settingsPath)) File.Copy(legacySettings, _settingsPath, false);
        if (File.Exists(_targetPath)) return;

        var legacyProfiles = Path.Combine(_legacyRoot, "profiles.json");
        try
        {
            if (!File.Exists(legacyProfiles)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(legacyProfiles));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var profile in document.RootElement.EnumerateArray())
            {
                var executablePath = ReadString(profile, "ExecutablePath");
                var title = ReadString(profile, "WindowTitleContains");
                var isZmbox = title.Contains("造梦盒子", StringComparison.OrdinalIgnoreCase) ||
                              Path.GetFileNameWithoutExtension(executablePath).Contains("造梦盒子", StringComparison.OrdinalIgnoreCase);
                if (!isZmbox) continue;
                SaveZmboxTarget(new ZmboxTargetSettings
                {
                    ExecutablePath = executablePath,
                    WindowTitleContains = string.IsNullOrWhiteSpace(title) ? "造梦盒子" : title,
                    BackgroundCapability = ReadBackgroundCapability(profile)
                });
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // The legacy files stay untouched. The new target starts with safe defaults.
        }
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static BackgroundCapability ReadBackgroundCapability(JsonElement profile)
    {
        if (!profile.TryGetProperty("BackgroundCapability", out var value)) return BackgroundCapability.Unknown;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) && Enum.IsDefined(typeof(BackgroundCapability), number) => (BackgroundCapability)number,
            JsonValueKind.String when Enum.TryParse<BackgroundCapability>(value.GetString(), true, out var parsed) => parsed,
            _ => BackgroundCapability.Unknown
        };
    }
}
