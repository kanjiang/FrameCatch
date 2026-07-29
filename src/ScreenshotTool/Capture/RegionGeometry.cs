using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace ScreenshotTool.Capture;

public static class RegionGeometry
{
    public static Int32Rect GetBoundingRect(IReadOnlyList<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return Int32Rect.Empty;
        }

        var minX = points[0].X;
        var minY = points[0].Y;
        var maxX = points[0].X;
        var maxY = points[0].Y;

        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            if (point.X < minX)
            {
                minX = point.X;
            }

            if (point.Y < minY)
            {
                minY = point.Y;
            }

            if (point.X > maxX)
            {
                maxX = point.X;
            }

            if (point.Y > maxY)
            {
                maxY = point.Y;
            }
        }

        var left = (int)Math.Floor(minX);
        var top = (int)Math.Floor(minY);
        var right = (int)Math.Ceiling(maxX);
        var bottom = (int)Math.Ceiling(maxY);
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static WriteableBitmap CropRectangle(BitmapSource source, Int32Rect rect)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clipped = ClipRect(rect, source.PixelWidth, source.PixelHeight);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            throw new ArgumentException("Rectangle must intersect the source image.", nameof(rect));
        }

        var cropped = new CroppedBitmap(EnsureBgra32(source), clipped);
        return new WriteableBitmap(cropped);
    }

    public static WriteableBitmap? CropLasso(BitmapSource source, IReadOnlyList<Point> points)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 3)
        {
            return null;
        }

        var bounds = GetBoundingRect(points);
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return null;
        }

        var clipped = ClipRect(bounds, source.PixelWidth, source.PixelHeight);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return null;
        }

        var cropped = new CroppedBitmap(EnsureBgra32(source), clipped);
        var result = new WriteableBitmap(cropped);
        var stride = result.BackBufferStride;
        var pixels = new byte[result.PixelHeight * stride];
        result.CopyPixels(pixels, stride, 0);

        for (var y = 0; y < result.PixelHeight; y++)
        {
            for (var x = 0; x < result.PixelWidth; x++)
            {
                var sampleX = clipped.X + x + 0.5;
                var sampleY = clipped.Y + y + 0.5;
                if (!ContainsPoint(points, sampleX, sampleY))
                {
                    var offset = y * stride + x * 4;
                    pixels[offset] = 0;
                    pixels[offset + 1] = 0;
                    pixels[offset + 2] = 0;
                    pixels[offset + 3] = 0;
                }
            }
        }

        result.WritePixels(new Int32Rect(0, 0, result.PixelWidth, result.PixelHeight), pixels, stride, 0);
        return result;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        return source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
    }

    private static Int32Rect ClipRect(Int32Rect rect, int maxWidth, int maxHeight)
    {
        var left = Math.Clamp(rect.X, 0, maxWidth);
        var top = Math.Clamp(rect.Y, 0, maxHeight);
        var right = Math.Clamp(rect.X + rect.Width, 0, maxWidth);
        var bottom = Math.Clamp(rect.Y + rect.Height, 0, maxHeight);
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static bool ContainsPoint(IReadOnlyList<Point> points, double x, double y)
    {
        var inside = false;
        var previous = points[points.Count - 1];

        for (var i = 0; i < points.Count; i++)
        {
            var current = points[i];
            var intersects = (current.Y > y) != (previous.Y > y) &&
                             x < (previous.X - current.X) * (y - current.Y) / (previous.Y - current.Y) + current.X;
            if (intersects)
            {
                inside = !inside;
            }

            previous = current;
        }

        return inside;
    }
}
