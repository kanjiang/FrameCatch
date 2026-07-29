using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Capture;

namespace ScreenshotTool.Tests;

public class RegionGeometryTests
{
    private static WriteableBitmap SolidBitmap(int width, int height, Color color)
    {
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }

        bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return bmp;
    }

    [Fact]
    public void GetBoundingRect_ReturnsExpectedBounds()
    {
        var rect = RegionGeometry.GetBoundingRect(new[]
        {
            new Point(1.2, 2.8),
            new Point(4.9, 6.1)
        });

        Assert.Equal(new Int32Rect(1, 2, 4, 5), rect);
    }

    [Fact]
    public void CropRectangle_ReturnsExpectedSizeAndPixels()
    {
        var src = SolidBitmap(100, 80, Colors.Red);

        var cropped = RegionGeometry.CropRectangle(src, new Int32Rect(10, 10, 30, 20));

        Assert.Equal(30, cropped.PixelWidth);
        Assert.Equal(20, cropped.PixelHeight);
        Assert.Equal(255, SampleAlpha(cropped, 0, 0));
        Assert.Equal(Colors.Red, SampleColor(cropped, 0, 0));
    }

    [Fact]
    public void CropLasso_MasksOutsidePixels()
    {
        var src = SolidBitmap(50, 50, Colors.Blue);
        var points = new[]
        {
            new Point(10, 10),
            new Point(40, 10),
            new Point(10, 40)
        };

        var cropped = RegionGeometry.CropLasso(src, points);

        Assert.NotNull(cropped);
        Assert.Equal(30, cropped!.PixelWidth);
        Assert.Equal(30, cropped.PixelHeight);
        Assert.Equal(0, SampleAlpha(cropped, 25, 25));
        Assert.Equal(255, SampleAlpha(cropped, 5, 5));
        Assert.Equal(Colors.Blue, SampleColor(cropped, 5, 5));
    }

    [Fact]
    public void CropLasso_TooShort_ReturnsNull()
    {
        var src = SolidBitmap(20, 20, Colors.Red);

        var cropped = RegionGeometry.CropLasso(src, new[] { new Point(1, 1), new Point(2, 1) });

        Assert.Null(cropped);
    }

    private static byte SampleAlpha(WriteableBitmap bmp, int x, int y)
    {
        var pixels = new byte[4];
        bmp.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return pixels[3];
    }

    private static Color SampleColor(WriteableBitmap bmp, int x, int y)
    {
        var pixels = new byte[4];
        bmp.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }
}
