using ZmboxZmx4Assist.Interop;

namespace ZmboxZmx4Assist.Tests;

/// <summary>
/// 后台窗口消息通道依赖 Windows 完整性级别：低完整性进程投递的消息会被 UIPI 拒绝。这些测试
/// 钉住 RID→级别映射与失败说明，保证通道被拒绝时用户拿到的是一条可执行的修复提示，而不是
/// 一句“目标窗口拒绝了输入消息”。
/// </summary>
[TestClass]
public sealed class ProcessIntegrityTests
{
    [TestMethod]
    public void BandFromRid_MapsTheWindowsIntegrityThresholds()
    {
        Assert.AreEqual(IntegrityBand.Untrusted, ProcessIntegrity.BandFromRid(0u));
        Assert.AreEqual(IntegrityBand.Untrusted, ProcessIntegrity.BandFromRid(0x0FFFu));
        Assert.AreEqual(IntegrityBand.Low, ProcessIntegrity.BandFromRid(0x1000u));
        Assert.AreEqual(IntegrityBand.Low, ProcessIntegrity.BandFromRid(0x1FFFu));
        Assert.AreEqual(IntegrityBand.Medium, ProcessIntegrity.BandFromRid(0x2000u));
        Assert.AreEqual(IntegrityBand.Medium, ProcessIntegrity.BandFromRid(0x2FFFu));
        Assert.AreEqual(IntegrityBand.High, ProcessIntegrity.BandFromRid(0x3000u));
        Assert.AreEqual(IntegrityBand.System, ProcessIntegrity.BandFromRid(0x4000u));
        Assert.AreEqual(IntegrityBand.System, ProcessIntegrity.BandFromRid(0x4FFFu));
    }

    [TestMethod]
    public void DescribeWindowMessageFailure_ExplainsUipiAndNamesTheFix()
    {
        var text = ProcessIntegrity.DescribeWindowMessageFailure(5, IntegrityBand.Low, IntegrityBand.Medium, @"C:\Games\tool");

        StringAssert.Contains(text, "UIPI");
        StringAssert.Contains(text, "低完整性");
        StringAssert.Contains(text, "中完整性");
        StringAssert.Contains(text, "/setintegritylevel (OI)(CI)Medium");
        StringAssert.Contains(text, @"C:\Games\tool");
    }

    [TestMethod]
    public void DescribeWindowMessageFailure_SameLevelDenialDoesNotBlameIntegrity()
    {
        var text = ProcessIntegrity.DescribeWindowMessageFailure(5, IntegrityBand.Medium, IntegrityBand.Medium, null);

        StringAssert.Contains(text, "错误 5");
        Assert.IsFalse(text.Contains("UIPI", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public void DescribeWindowMessageFailure_OtherErrorsKeepTheWin32Code()
    {
        var text = ProcessIntegrity.DescribeWindowMessageFailure(6, IntegrityBand.Medium, IntegrityBand.High, null);

        StringAssert.Contains(text, "错误 6");
        StringAssert.Contains(text, "中完整性");
        StringAssert.Contains(text, "高完整性");
        Assert.IsFalse(text.Contains("UIPI", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public void DescribeWindowMessageFailure_UnknownTargetStillProducesAMessage()
    {
        var text = ProcessIntegrity.DescribeWindowMessageFailure(87, IntegrityBand.Unknown, IntegrityBand.Unknown, null);

        StringAssert.Contains(text, "错误 87");
        Assert.IsFalse(text.Contains("UIPI", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public void Describe_NamesEveryBand()
    {
        Assert.AreEqual("低", ProcessIntegrity.Describe(IntegrityBand.Low));
        Assert.AreEqual("中", ProcessIntegrity.Describe(IntegrityBand.Medium));
        Assert.AreEqual("高", ProcessIntegrity.Describe(IntegrityBand.High));
        Assert.AreEqual("系统", ProcessIntegrity.Describe(IntegrityBand.System));
        Assert.AreEqual("不受信任", ProcessIntegrity.Describe(IntegrityBand.Untrusted));
        Assert.AreEqual("未知", ProcessIntegrity.Describe(IntegrityBand.Unknown));
    }

    [TestMethod]
    public void OfCurrentProcess_ReadsARealIntegrityLevelOnWindows()
    {
        // 这条测试同时验证 P/Invoke 链路（OpenProcess → OpenProcessToken → GetTokenInformation）
        // 没有写错；任何 Windows 用户令牌都能读到自己进程的完整性 SID。
        Assert.AreNotEqual(IntegrityBand.Unknown, ProcessIntegrity.OfCurrentProcess());
    }
}
