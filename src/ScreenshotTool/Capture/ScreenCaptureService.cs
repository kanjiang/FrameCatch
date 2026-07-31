using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Native;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ScreenshotTool.Capture;

public static class ScreenCaptureService
{
    public readonly record struct VirtualScreenInfo(int X, int Y, int Width, int Height);

    public static VirtualScreenInfo GetVirtualScreen() => new(
        Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));

    public static BitmapSource CaptureVirtualScreen()
    {
        var vs = GetVirtualScreen();
        using var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(vs.X, vs.Y, 0, 0, new System.Drawing.Size(vs.Width, vs.Height));
        }

        return ToBitmapSource(bmp);
    }

    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var wb = new WriteableBitmap(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, bmp.Width, bmp.Height), data.Scan0, data.Stride * bmp.Height, data.Stride);
            wb.Freeze();
            return wb;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
