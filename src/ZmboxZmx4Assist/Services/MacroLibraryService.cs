using System.Text.Json;
using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Services;

public sealed class MacroLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _root;
    private readonly string _macros;
    private readonly string _profilesPath;
    private readonly string _settingsPath;
    private readonly string _targetPath;
    private readonly string? _legacyRoot;

    public MacroLibraryService(string? root = null, string? legacyRoot = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _root = root ?? Path.Combine(local, "ZmboxZmx4Assist");
        _legacyRoot = legacyRoot ?? (root is null ? Path.Combine(local, "GameMacro") : null);
        _macros = Path.Combine(_root, "macros");
        _profilesPath = Path.Combine(_root, "profiles.json");
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
            return JsonSerializer.Deserialize<ZmboxTargetSettings>(File.ReadAllText(_targetPath), JsonOptions) ?? new ZmboxTargetSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new ZmboxTargetSettings();
        }
    }

    public void SaveZmboxTarget(ZmboxTargetSettings settings) => WriteJsonAtomically(_targetPath, settings);

    public IReadOnlyList<TargetProfile> LoadProfiles()
    {
        if (!File.Exists(_profilesPath))
        {
            var defaults = new[] { new TargetProfile() };
            SaveProfiles(defaults);
            return defaults;
        }
        var profiles = JsonSerializer.Deserialize<List<TargetProfile>>(File.ReadAllText(_profilesPath), JsonOptions) ?? [];
        // 360 Game Hall has no useful background-input path here. Retire only the historical
        // preset by its original name; macros are deliberately not touched and remain loadable.
        var migrated = profiles
            .Where(profile => !string.Equals(profile.Name, "360 造梦西游4", StringComparison.Ordinal))
            .ToArray();
        if (migrated.Length == 0) migrated = [new TargetProfile()];
        if (!profiles.SequenceEqual(migrated)) SaveProfiles(migrated);
        return migrated;
    }

    public void SaveProfiles(IEnumerable<TargetProfile> profiles) => WriteJsonAtomically(_profilesPath, profiles);

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
                macros.Add(macro);
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
            var profiles = File.Exists(legacyProfiles)
                ? JsonSerializer.Deserialize<List<TargetProfile>>(File.ReadAllText(legacyProfiles), JsonOptions) ?? []
                : [];
            var zmbox = profiles.FirstOrDefault(profile =>
                profile.WindowTitleContains.Contains("造梦盒子", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(profile.ExecutablePath).Contains("造梦盒子", StringComparison.OrdinalIgnoreCase));
            if (zmbox is not null)
                SaveZmboxTarget(new ZmboxTargetSettings
                {
                    ExecutablePath = zmbox.ExecutablePath,
                    WindowTitleContains = string.IsNullOrWhiteSpace(zmbox.WindowTitleContains) ? "造梦盒子" : zmbox.WindowTitleContains,
                    BackgroundCapability = zmbox.BackgroundCapability
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // The legacy files stay untouched. The new target starts with safe defaults.
        }
    }
}
