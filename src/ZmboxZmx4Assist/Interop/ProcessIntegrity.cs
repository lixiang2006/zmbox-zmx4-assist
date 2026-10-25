using System.Runtime.InteropServices;

namespace ZmboxZmx4Assist.Interop;

/// <summary>
/// Windows 强制完整性控制（MIC）的完整性级别。进程以启动映像的完整性标签运行：一个从低完整性
/// 目录启动的 exe 会一直以低完整性运行，而 UIPI 又禁止低完整性进程向高完整性窗口投递消息。
/// 这是后台窗口消息通道最常见的“目标窗口拒绝了输入消息”来源，本类型负责把原因说清楚。
/// </summary>
internal enum IntegrityBand
{
    Unknown = 0,
    Untrusted,
    Low,
    Medium,
    High,
    System
}

internal static class ProcessIntegrity
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int ErrorAccessDenied = 5;

    /// <summary>Maps a token integrity RID to a band; anything below low is untrusted.</summary>
    internal static IntegrityBand BandFromRid(uint rid) => rid switch
    {
        < 0x1000 => IntegrityBand.Untrusted,
        < 0x2000 => IntegrityBand.Low,
        < 0x3000 => IntegrityBand.Medium,
        < 0x4000 => IntegrityBand.High,
        _ => IntegrityBand.System
    };

    internal static IntegrityBand OfCurrentProcess() => OfProcess(Environment.ProcessId);

    /// <summary>Reads the integrity level of a process; unknown when the token cannot be queried.</summary>
    internal static IntegrityBand OfProcess(int processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return IntegrityBand.Unknown;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return IntegrityBand.Unknown;
            try
            {
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
                if (size == 0) return IntegrityBand.Unknown;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) return IntegrityBand.Unknown;
                    var label = Marshal.PtrToStructure<SidAndAttributes>(buffer);
                    if (label.Sid == IntPtr.Zero) return IntegrityBand.Unknown;
                    var count = GetSidSubAuthorityCount(label.Sid);
                    if (count == IntPtr.Zero || Marshal.ReadByte(count) == 0) return IntegrityBand.Unknown;
                    return BandFromRid((uint)Marshal.ReadInt32(GetSidSubAuthority(label.Sid, 0)));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    internal static string Describe(IntegrityBand band) => band switch
    {
        IntegrityBand.Untrusted => "不受信任",
        IntegrityBand.Low => "低",
        IntegrityBand.Medium => "中",
        IntegrityBand.High => "高",
        IntegrityBand.System => "系统",
        _ => "未知"
    };

    /// <summary>
    /// Turns a rejected window message into an actionable message. The destructive case is a lower
    /// integrity process posting to a higher one: the Win32 error is "access denied" and no amount
    /// of retrying helps, so the text names both levels and the exact fix.
    /// </summary>
    internal static string DescribeWindowMessageFailure(int errorCode, IntegrityBand own, IntegrityBand target, string? processDirectory)
    {
        if (errorCode == ErrorAccessDenied && own != IntegrityBand.Unknown && target != IntegrityBand.Unknown && (int)own < (int)target)
        {
            var directory = string.IsNullOrWhiteSpace(processDirectory) ? "<程序所在目录>" : processDirectory;
            return $"后台通道被 Windows 的完整性隔离（UIPI）拦截：本程序以{Describe(own)}完整性运行，目标窗口属于{Describe(target)}完整性进程，" +
                "系统禁止低完整性进程向高完整性窗口投递消息。请以管理员身份重新运行本程序；" +
                $"或在终端执行 icacls \"{directory}\" /setintegritylevel (OI)(CI)Medium 后重启本程序。";
        }

        if (errorCode == ErrorAccessDenied)
            return "后台通道被 Windows 拒绝（错误 5：拒绝访问）。目标窗口可能属于权限更高的进程，请以管理员身份重新运行本程序后重试。";

        var levels = own == IntegrityBand.Unknown || target == IntegrityBand.Unknown
            ? string.Empty
            : $"。本程序：{Describe(own)}完整性，目标进程：{Describe(target)}完整性";
        return $"目标窗口拒绝了输入消息（Win32 错误 {errorCode}{levels}）。";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, uint length, out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
}
