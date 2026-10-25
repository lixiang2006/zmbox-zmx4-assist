using ZmboxZmx4Assist.Interop;
using ZmboxZmx4Assist.Utilities;

namespace ZmboxZmx4Assist.Tests;

[TestClass]
public sealed class InputTranslationTests
{
    [TestMethod]
    public void MouseCoordinates_PrimaryScreen_MapsCornersToAbsoluteRange()
    {
        var bounds = new VirtualScreenBounds(0, 0, 1920, 1080);

        Assert.AreEqual((0, 0), MouseCoordinateTranslator.ToAbsolute(0, 0, bounds));
        Assert.AreEqual((65535, 65535), MouseCoordinateTranslator.ToAbsolute(1919, 1079, bounds));
        var (middleX, middleY) = MouseCoordinateTranslator.ToAbsolute(960, 540, bounds);
        Assert.IsTrue(Math.Abs(middleX - 32767) <= 32, $"middle x was {middleX}");
        Assert.IsTrue(Math.Abs(middleY - 32767) <= 32, $"middle y was {middleY}");
    }

    [TestMethod]
    public void MouseCoordinates_SecondaryScreenLeftOfPrimary_UsesVirtualDesktopOrigin()
    {
        var bounds = new VirtualScreenBounds(-1920, 0, 3840, 1080);

        Assert.AreEqual((0, 0), MouseCoordinateTranslator.ToAbsolute(-1920, 0, bounds));
        var (middleX, middleY) = MouseCoordinateTranslator.ToAbsolute(0, 540, bounds);
        Assert.IsTrue(Math.Abs(middleX - 32767) <= 32, $"middle x was {middleX}");
        Assert.IsTrue(Math.Abs(middleY - 32767) <= 32, $"middle y was {middleY}");
        Assert.AreEqual((65535, 65535), MouseCoordinateTranslator.ToAbsolute(1919, 1079, bounds));
    }

    [TestMethod]
    public void MouseCoordinates_OutsideVirtualDesktop_ClampsToAbsoluteRange()
    {
        var bounds = new VirtualScreenBounds(0, 0, 1920, 1080);

        Assert.AreEqual((0, 0), MouseCoordinateTranslator.ToAbsolute(-500, -500, bounds));
        Assert.AreEqual((65535, 65535), MouseCoordinateTranslator.ToAbsolute(5000, 5000, bounds));
    }

    [TestMethod]
    public void KeyLParam_ExtendedKeyAndTransitionBits_AreSetPerMessage()
    {
        var down = (long)NativeMethods.MakeKeyLParam(false, 0x25);
        var up = (long)NativeMethods.MakeKeyLParam(true, 0x25);

        Assert.AreEqual(1L, down & 0xFFFF, "repeat count");
        Assert.AreNotEqual(0L, (down >> 16) & 0xFF, "scan code");
        Assert.AreEqual(1L, (down >> 24) & 1, "extended flag for VK_LEFT");
        Assert.AreEqual(0L, (down >> 31) & 1, "key-down transition bit");
        Assert.AreEqual(3L, (up >> 30) & 3, "key-up transition and previous-state bits");
    }

    [TestMethod]
    public void KeyLParam_NonExtendedKey_DoesNotSetExtendedBit()
    {
        var lParam = (long)NativeMethods.MakeKeyLParam(false, 0x57);

        Assert.AreEqual(0L, (lParam >> 24) & 1);
        Assert.AreNotEqual(0L, (lParam >> 16) & 0xFF);
    }

    [TestMethod]
    public void IsExtendedKey_MatchesArrowsAndHandedModifiers()
    {
        Assert.IsTrue(NativeMethods.IsExtendedKey(0x25));
        Assert.IsTrue(NativeMethods.IsExtendedKey(0x2E));
        Assert.IsTrue(NativeMethods.IsExtendedKey(0xA3));
        Assert.IsFalse(NativeMethods.IsExtendedKey(0x57));
        Assert.IsFalse(NativeMethods.IsExtendedKey(0xA2));
    }

    [TestMethod]
    public void WheelLParam_KeepsScreenCoordinatesInsteadOfClientCoordinates()
    {
        var lParam = (long)NativeMethods.MakeWheelLParam(300, 1200);

        Assert.AreEqual(300L, lParam & 0xFFFF, "x stays in screen coordinates");
        Assert.AreEqual(1200L, (lParam >> 16) & 0xFFFF, "y stays in screen coordinates");
    }

    [TestMethod]
    public void WheelLParam_NegativeCoordinate_IsEncodedAsSignedLowWord()
    {
        var lParam = (long)NativeMethods.MakeWheelLParam(-5, 0);

        Assert.AreEqual(65531L, lParam & 0xFFFF);
    }
}
