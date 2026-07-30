using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Editor;

namespace ScreenshotTool.Tests;

public class ImageExportServiceTests
{
    [Fact]
    public void Compose_WithoutAnnotations_PreservesBasePixels()
    {
        var baseImage = CreateSolidBitmap(24, 18, Colors.White);

        var composed = ImageExportService.Compose(baseImage, []);

        Assert.Equal(24, composed.PixelWidth);
        Assert.Equal(18, composed.PixelHeight);
        Assert.Equal(Colors.White, SampleColor(composed, 12, 9));
    }

    [Fact]
    public void Compose_WithRectangleAnnotation_DrawsOverlayOnTop()
    {
        var baseImage = CreateSolidBitmap(30, 30, Colors.White);
        var annotations = new AnnotationItem[]
        {
            new RectAnnotation(Guid.NewGuid(), new Rect(6, 6, 18, 18), Colors.Red, 4)
        };

        var composed = ImageExportService.Compose(baseImage, annotations);

        var edgePixel = SampleColor(composed, 15, 6);
        var centerPixel = SampleColor(composed, 15, 15);

        Assert.True(edgePixel.R > 200, "expected top edge to be visibly red");
        Assert.True(edgePixel.R > edgePixel.G, "expected top edge to favor red over green");
        Assert.Equal(Colors.White, centerPixel);
    }

    [Fact]
    public void Compose_WithExcludeId_SkipsMatchingAnnotation()
    {
        var baseImage = CreateSolidBitmap(30, 30, Colors.White);
        var keptId = Guid.NewGuid();
        var skippedId = Guid.NewGuid();
        var annotations = new AnnotationItem[]
        {
            new RectAnnotation(keptId, new Rect(6, 6, 18, 18), Colors.Red, 4),
            new RectAnnotation(skippedId, new Rect(2, 2, 4, 4), Colors.Blue, 4)
        };

        // Compose has no exclude overload; verify DrawAnnotations exclude via visual render.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(baseImage, new Rect(0, 0, 30, 30));
            ImageExportService.DrawAnnotations(dc, annotations, excludeId: skippedId);
        }

        var bitmap = new RenderTargetBitmap(30, 30, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var redEdge = SampleColor(bitmap, 15, 6);
        var blueCorner = SampleColor(bitmap, 3, 3);

        Assert.True(redEdge.R > 200, "kept red annotation should still draw");
        Assert.Equal(Colors.White, blueCorner);
    }

    private static WriteableBitmap CreateSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return bitmap;
    }

    private static Color SampleColor(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }
}
