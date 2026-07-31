using System.Windows;
using ScreenshotTool.Capture;

namespace ScreenshotTool.Tests;

public class CaptureOverlaySelectionTests
{
    [Fact]
    public void NormalizeRectangle_ReturnsExpectedBounds_WhenDraggingBackwards()
    {
        var rect = CaptureOverlaySelection.NormalizeRectangle(
            new Point(80.4, 60.2),
            new Point(10.1, 20.9));

        Assert.Equal(new Int32Rect(10, 20, 71, 41), rect);
    }

    [Fact]
    public void IsValidLasso_ReturnsFalse_WhenPathTooShort()
    {
        var isValid = CaptureOverlaySelection.IsValidLasso(new[]
        {
            new Point(10, 10),
            new Point(10.8, 10.5),
            new Point(11.2, 10.7)
        });

        Assert.False(isValid);
    }

    [Fact]
    public void GetConfirmationBarPosition_ClampsWithinSurface()
    {
        var origin = CaptureOverlaySelection.GetConfirmationBarPosition(
            new Int32Rect(260, 180, 35, 20),
            new Size(120, 40),
            new Size(300, 200));

        Assert.Equal(new Point(168, 128), origin);
    }
}
