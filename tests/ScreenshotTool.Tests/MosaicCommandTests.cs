using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Editor;

namespace ScreenshotTool.Tests;

public class MosaicCommandTests
{
    [Fact]
    public void Mosaic_ChangesPixels_UndoRestores()
    {
        var bmp = CreateSolid(32, 32, Colors.Red);
        SetPixel(bmp, 16, 16, Colors.Blue);
        var before = GetPixel(bmp, 16, 16);

        var cmd = new MosaicCommand(bmp, new Int32Rect(8, 8, 16, 16), blockSize: 8);
        var stack = new UndoStack();

        stack.Execute(cmd);

        var mid = GetPixel(bmp, 16, 16);
        Assert.NotEqual(before, mid);

        stack.Undo();
        Assert.Equal(before, GetPixel(bmp, 16, 16));
    }

    private static WriteableBitmap CreateSolid(int width, int height, Color color)
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

    private static void SetPixel(WriteableBitmap bmp, int x, int y, Color color)
    {
        var pixels = new byte[4]
        {
            color.B,
            color.G,
            color.R,
            color.A
        };

        bmp.WritePixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
    }

    private static Color GetPixel(WriteableBitmap bmp, int x, int y)
    {
        var pixels = new byte[4];
        bmp.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }
}
