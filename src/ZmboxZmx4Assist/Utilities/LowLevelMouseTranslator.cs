using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Utilities;

public static class LowLevelMouseTranslator
{
    public static MouseButtonKind XButtonFromMouseData(uint mouseData) => ((mouseData >> 16) & 0xffff) switch
    {
        1 => MouseButtonKind.X1,
        2 => MouseButtonKind.X2,
        _ => MouseButtonKind.None
    };
}
