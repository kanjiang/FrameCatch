using ScreenshotTool.Native;
using ScreenshotTool.Shell;

namespace ScreenshotTool.Tests;

public class HotkeyServiceTests
{
    [Fact]
    public void MatchesRegisteredHotkeyMessage_WhenMessageAndIdMatch_ReturnsTrue()
    {
        var matches = HotkeyService.MatchesRegisteredHotkeyMessage(
            Win32.WM_HOTKEY,
            (IntPtr)HotkeyService.RegisteredHotkeyId);

        Assert.True(matches);
    }

    [Fact]
    public void MatchesRegisteredHotkeyMessage_WhenMessageOrIdDiffers_ReturnsFalse()
    {
        Assert.False(HotkeyService.MatchesRegisteredHotkeyMessage(0, (IntPtr)HotkeyService.RegisteredHotkeyId));
        Assert.False(HotkeyService.MatchesRegisteredHotkeyMessage(Win32.WM_HOTKEY, (IntPtr)1234));
    }
}
