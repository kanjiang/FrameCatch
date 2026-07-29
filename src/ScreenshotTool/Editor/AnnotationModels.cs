using System.Windows;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace ScreenshotTool.Editor;

public enum ToolKind
{
    Select,
    Rectangle,
    Ellipse,
    Arrow,
    Pen,
    Highlighter,
    Mosaic,
    Text
}

public abstract record AnnotationItem(Guid Id);

public sealed record RectAnnotation(
    Guid Id,
    Rect Bounds,
    Color StrokeColor,
    double StrokeThickness) : AnnotationItem(Id);

public sealed record EllipseAnnotation(
    Guid Id,
    Rect Bounds,
    Color StrokeColor,
    double StrokeThickness) : AnnotationItem(Id);

public sealed record ArrowAnnotation(
    Guid Id,
    Point Start,
    Point End,
    Color StrokeColor,
    double StrokeThickness) : AnnotationItem(Id);

public sealed record PathAnnotation(
    Guid Id,
    IReadOnlyList<Point> Points,
    Color StrokeColor,
    double StrokeThickness,
    bool IsHighlighter) : AnnotationItem(Id);

public sealed record TextAnnotation(
    Guid Id,
    Point Position,
    string Text,
    Color TextColor,
    double FontSize) : AnnotationItem(Id);
