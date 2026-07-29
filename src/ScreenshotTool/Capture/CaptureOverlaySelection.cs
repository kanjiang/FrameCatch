using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ScreenshotTool.Capture;

public static class CaptureOverlaySelection
{
    private const int MinimumSelectionPixels = 2;
    private const double ConfirmationMargin = 12;

    public static Int32Rect NormalizeRectangle(Point start, Point end)
    {
        var left = (int)Math.Floor(Math.Min(start.X, end.X));
        var top = (int)Math.Floor(Math.Min(start.Y, end.Y));
        var right = (int)Math.Ceiling(Math.Max(start.X, end.X));
        var bottom = (int)Math.Ceiling(Math.Max(start.Y, end.Y));
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static bool IsValidRectangle(Int32Rect rect) =>
        rect.Width >= MinimumSelectionPixels && rect.Height >= MinimumSelectionPixels;

    public static bool IsValidLasso(IReadOnlyList<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 3)
        {
            return false;
        }

        var bounds = RegionGeometry.GetBoundingRect(points);
        return IsValidRectangle(bounds);
    }

    public static Point GetConfirmationBarPosition(Int32Rect selectionBounds, Size barSize, Size surfaceSize)
    {
        var proposedX = selectionBounds.X + selectionBounds.Width - barSize.Width;
        var proposedY = selectionBounds.Y + selectionBounds.Height + ConfirmationMargin;

        if (proposedY + barSize.Height > surfaceSize.Height - ConfirmationMargin)
        {
            proposedY = selectionBounds.Y - barSize.Height - ConfirmationMargin;
        }

        if (proposedY < ConfirmationMargin)
        {
            proposedY = ConfirmationMargin;
        }

        var maxX = Math.Max(ConfirmationMargin, surfaceSize.Width - barSize.Width - ConfirmationMargin);
        var maxY = Math.Max(ConfirmationMargin, surfaceSize.Height - barSize.Height - ConfirmationMargin);

        proposedX = Math.Clamp(proposedX, ConfirmationMargin, maxX);
        proposedY = Math.Clamp(proposedY, ConfirmationMargin, maxY);
        return new Point(proposedX, proposedY);
    }
}
