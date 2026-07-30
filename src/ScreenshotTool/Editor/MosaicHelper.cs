using System.Windows;
using System.Windows.Media.Imaging;

namespace ScreenshotTool.Editor;

public static class MosaicHelper
{
    public static void ApplyMosaic(WriteableBitmap bitmap, Int32Rect rect, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        if (bitmap.IsFrozen)
        {
            throw new InvalidOperationException("Cannot apply mosaic to a frozen bitmap.");
        }

        var clipped = ClipRect(rect, bitmap.PixelWidth, bitmap.PixelHeight);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        const int bytesPerPixel = 4;
        var stride = clipped.Width * bytesPerPixel;
        var pixels = new byte[checked(clipped.Height * stride)];
        bitmap.CopyPixels(clipped, pixels, stride, 0);

        for (var blockTop = 0; blockTop < clipped.Height; blockTop += blockSize)
        {
            var blockBottom = Math.Min(blockTop + blockSize, clipped.Height);
            for (var blockLeft = 0; blockLeft < clipped.Width; blockLeft += blockSize)
            {
                var blockRight = Math.Min(blockLeft + blockSize, clipped.Width);
                var blockPixelCount = 0;
                long sumB = 0;
                long sumG = 0;
                long sumR = 0;
                long sumA = 0;

                for (var y = blockTop; y < blockBottom; y++)
                {
                    var rowOffset = y * stride;
                    for (var x = blockLeft; x < blockRight; x++)
                    {
                        var offset = rowOffset + x * bytesPerPixel;
                        sumB += pixels[offset];
                        sumG += pixels[offset + 1];
                        sumR += pixels[offset + 2];
                        sumA += pixels[offset + 3];
                        blockPixelCount++;
                    }
                }

                if (blockPixelCount == 0)
                {
                    continue;
                }

                var avgB = (byte)(sumB / blockPixelCount);
                var avgG = (byte)(sumG / blockPixelCount);
                var avgR = (byte)(sumR / blockPixelCount);
                var avgA = (byte)(sumA / blockPixelCount);

                for (var y = blockTop; y < blockBottom; y++)
                {
                    var rowOffset = y * stride;
                    for (var x = blockLeft; x < blockRight; x++)
                    {
                        var offset = rowOffset + x * bytesPerPixel;
                        pixels[offset] = avgB;
                        pixels[offset + 1] = avgG;
                        pixels[offset + 2] = avgR;
                        pixels[offset + 3] = avgA;
                    }
                }
            }
        }

        bitmap.WritePixels(clipped, pixels, stride, 0);
    }

    public static byte[] CapturePixels(WriteableBitmap bitmap, Int32Rect rect)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var clipped = ClipRect(rect, bitmap.PixelWidth, bitmap.PixelHeight);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return Array.Empty<byte>();
        }

        const int bytesPerPixel = 4;
        var stride = clipped.Width * bytesPerPixel;
        var pixels = new byte[clipped.Height * stride];
        bitmap.CopyPixels(clipped, pixels, stride, 0);
        return pixels;
    }

    public static void RestorePixels(WriteableBitmap bitmap, Int32Rect rect, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(pixels);
        if (bitmap.IsFrozen)
        {
            throw new InvalidOperationException("Cannot restore pixels on a frozen bitmap.");
        }

        var clipped = ClipRect(rect, bitmap.PixelWidth, bitmap.PixelHeight);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        const int bytesPerPixel = 4;
        var stride = clipped.Width * bytesPerPixel;
        bitmap.WritePixels(clipped, pixels, stride, 0);
    }

    private static Int32Rect ClipRect(Int32Rect rect, int maxWidth, int maxHeight)
    {
        var left = Math.Clamp(rect.X, 0, maxWidth);
        var top = Math.Clamp(rect.Y, 0, maxHeight);
        var right = Math.Clamp(rect.X + rect.Width, 0, maxWidth);
        var bottom = Math.Clamp(rect.Y + rect.Height, 0, maxHeight);
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
