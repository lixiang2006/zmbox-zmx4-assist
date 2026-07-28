using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Utilities;

public static class MacroValidator
{
    public static string? Validate(MacroDefinition macro)
    {
        if (macro.Events.Count == 0) return "宏没有可回放的输入事件。";
        if (macro.Events.Any(x => x.OffsetMicroseconds < 0)) return "宏包含无效的时间戳。";
        if (!macro.Events.SequenceEqual(macro.Events.OrderBy(x => x.OffsetMicroseconds))) return "宏事件未按时间排序。";
        return null;
    }
}
