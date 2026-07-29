using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace ScreenshotTool.Editor;

public static class ImageExportService
{
    private const string ClipboardPngFormat = "PNG";
    private const int ClipboardRetryCount = 3;
    private const int ClipboardRetryDelayMs = 80;

    public static BitmapSource Compose(WriteableBitmap baseImage, IEnumerable<AnnotationItem> items)
    {
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(items);

        var visual = new DrawingVisual();
        using (var drawingContext = visual.RenderOpen())
        {
            drawingContext.DrawImage(baseImage, new Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
            DrawAnnotations(drawingContext, items);
        }

        var bitmap = new RenderTargetBitmap(
            baseImage.PixelWidth,
            baseImage.PixelHeight,
            baseImage.DpiX > 0 ? baseImage.DpiX : 96,
            baseImage.DpiY > 0 ? baseImage.DpiY : 96,
            PixelFormats.Pbgra32);

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void CopyToClipboard(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var lastError = default(Exception);
        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                using var pngStream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(pngStream);
                pngStream.Position = 0;

                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.Bitmap, image);
                dataObject.SetData(ClipboardPngFormat, pngStream);
                Clipboard.SetDataObject(dataObject, true);
                return;
            }
            catch (COMException ex)
            {
                lastError = ex;
            }
            catch (ExternalException ex)
            {
                lastError = ex;
            }

            Thread.Sleep(ClipboardRetryDelayMs);
        }

        throw new InvalidOperationException("复制到剪贴板失败，请重试。", lastError);
    }

    public static void SaveToFile(BitmapSource image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    internal static void DrawAnnotations(DrawingContext drawingContext, IEnumerable<AnnotationItem> items)
    {
        foreach (var item in items)
        {
            DrawAnnotation(drawingContext, item);
        }
    }

    internal static void DrawAnnotation(DrawingContext drawingContext, AnnotationItem item)
    {
        switch (item)
        {
            case RectAnnotation rect:
                drawingContext.DrawRectangle(null, CreatePen(rect.StrokeColor, rect.StrokeThickness), rect.Bounds);
                break;

            case EllipseAnnotation ellipse:
                drawingContext.DrawEllipse(
                    null,
                    CreatePen(ellipse.StrokeColor, ellipse.StrokeThickness),
                    ellipse.Bounds.Location + new Vector(ellipse.Bounds.Width / 2d, ellipse.Bounds.Height / 2d),
                    ellipse.Bounds.Width / 2d,
                    ellipse.Bounds.Height / 2d);
                break;

            case ArrowAnnotation arrow:
                DrawArrow(drawingContext, arrow);
                break;

            case PathAnnotation path:
                DrawPath(drawingContext, path);
                break;

            case TextAnnotation text:
                DrawText(drawingContext, text);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, "Unsupported annotation type.");
        }
    }

    internal static Geometry CreateGeometry(AnnotationItem item)
    {
        return item switch
        {
            RectAnnotation rect => new RectangleGeometry(rect.Bounds),
            EllipseAnnotation ellipse => new EllipseGeometry(ellipse.Bounds),
            ArrowAnnotation arrow => CreateArrowGeometry(arrow),
            PathAnnotation path => CreatePathGeometry(path.Points),
            TextAnnotation text => new RectangleGeometry(GetTextBounds(text)),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unsupported annotation type.")
        };
    }

    internal static Rect GetBounds(AnnotationItem item)
    {
        return item switch
        {
            RectAnnotation rect => rect.Bounds,
            EllipseAnnotation ellipse => ellipse.Bounds,
            ArrowAnnotation arrow => CreateArrowGeometry(arrow).Bounds,
            PathAnnotation path => CreatePathGeometry(path.Points).Bounds,
            TextAnnotation text => GetTextBounds(text),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unsupported annotation type.")
        };
    }

    internal static Pen CreatePen(Color color, double thickness, bool isHighlighter = false)
    {
        var effectiveColor = isHighlighter
            ? Color.FromArgb((byte)Math.Min(160, Math.Max(48, color.A == 0 ? 96 : color.A)), color.R, color.G, color.B)
            : color;

        var pen = new Pen(new SolidColorBrush(effectiveColor), Math.Max(1, thickness))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        if (pen.Brush.CanFreeze)
        {
            pen.Brush.Freeze();
        }

        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    private static void DrawArrow(DrawingContext drawingContext, ArrowAnnotation arrow)
    {
        var pen = CreatePen(arrow.StrokeColor, arrow.StrokeThickness);
        drawingContext.DrawLine(pen, arrow.Start, arrow.End);

        var vector = arrow.End - arrow.Start;
        if (vector.Length <= double.Epsilon)
        {
            return;
        }

        vector.Normalize();
        var headLength = Math.Max(12, arrow.StrokeThickness * 3);
        var headAngle = Math.PI / 7;
        var left = Rotate(vector * -headLength, headAngle);
        var right = Rotate(vector * -headLength, -headAngle);

        drawingContext.DrawLine(pen, arrow.End, arrow.End + left);
        drawingContext.DrawLine(pen, arrow.End, arrow.End + right);
    }

    private static void DrawPath(DrawingContext drawingContext, PathAnnotation path)
    {
        if (path.Points.Count == 0)
        {
            return;
        }

        if (path.Points.Count == 1)
        {
            drawingContext.DrawEllipse(
                new SolidColorBrush(path.StrokeColor),
                null,
                path.Points[0],
                path.StrokeThickness / 2d,
                path.StrokeThickness / 2d);
            return;
        }

        drawingContext.DrawGeometry(null, CreatePen(path.StrokeColor, path.StrokeThickness, path.IsHighlighter), CreatePathGeometry(path.Points));
    }

    private static void DrawText(DrawingContext drawingContext, TextAnnotation text)
    {
        var formattedText = new FormattedText(
            text.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            Math.Max(12, text.FontSize),
            new SolidColorBrush(text.TextColor),
            1.0);

        drawingContext.DrawText(formattedText, text.Position);
    }

    private static Geometry CreateArrowGeometry(ArrowAnnotation arrow)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(arrow.Start, isFilled: false, isClosed: false);
        context.LineTo(arrow.End, isStroked: true, isSmoothJoin: true);

        var vector = arrow.End - arrow.Start;
        if (vector.Length > double.Epsilon)
        {
            vector.Normalize();
            var headLength = Math.Max(12, arrow.StrokeThickness * 3);
            var headAngle = Math.PI / 7;
            context.BeginFigure(arrow.End, isFilled: false, isClosed: false);
            context.LineTo(arrow.End + Rotate(vector * -headLength, headAngle), isStroked: true, isSmoothJoin: true);
            context.BeginFigure(arrow.End, isFilled: false, isClosed: false);
            context.LineTo(arrow.End + Rotate(vector * -headLength, -headAngle), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreatePathGeometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0)
        {
            return geometry;
        }

        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            for (var i = 1; i < points.Count; i++)
            {
                context.LineTo(points[i], isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Rect GetTextBounds(TextAnnotation text)
    {
        var formattedText = new FormattedText(
            text.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            Math.Max(12, text.FontSize),
            new SolidColorBrush(text.TextColor),
            1.0);

        return new Rect(text.Position, new Size(formattedText.WidthIncludingTrailingWhitespace, formattedText.Height));
    }

    private static Vector Rotate(Vector vector, double radians)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Vector(
            vector.X * cos - vector.Y * sin,
            vector.X * sin + vector.Y * cos);
    }
}
