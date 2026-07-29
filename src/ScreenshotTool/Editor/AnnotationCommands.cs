using System.Windows;
using Point = System.Windows.Point;

namespace ScreenshotTool.Editor;

public interface IAnnotationCommand
{
    void Do();

    void Undo();
}

public sealed class AddAnnotationCommand : IAnnotationCommand
{
    private readonly IList<AnnotationItem> _items;
    private readonly AnnotationItem _item;

    public AddAnnotationCommand(IList<AnnotationItem> items, AnnotationItem item)
    {
        _items = items;
        _item = item;
    }

    public void Do() => _items.Add(_item);

    public void Undo() => _items.Remove(_item);
}

public sealed class RemoveAnnotationCommand : IAnnotationCommand
{
    private readonly IList<AnnotationItem> _items;
    private readonly AnnotationItem _item;
    private int _index = -1;

    public RemoveAnnotationCommand(IList<AnnotationItem> items, AnnotationItem item)
    {
        _items = items;
        _item = item;
    }

    public void Do()
    {
        _index = _items.IndexOf(_item);
        if (_index >= 0)
        {
            _items.RemoveAt(_index);
        }
    }

    public void Undo()
    {
        if (_index >= 0)
        {
            _items.Insert(_index, _item);
        }
    }
}

public sealed class MoveAnnotationCommand : IAnnotationCommand
{
    private readonly IList<AnnotationItem> _items;
    private readonly int _index;
    private readonly AnnotationItem _original;
    private readonly AnnotationItem _moved;

    public MoveAnnotationCommand(IList<AnnotationItem> items, int index, Vector delta)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, items.Count);

        _items = items;
        _index = index;
        _original = items[index];
        _moved = TranslateAnnotation(_original, delta);
    }

    public void Do() => _items[_index] = _moved;

    public void Undo() => _items[_index] = _original;

    private static AnnotationItem TranslateAnnotation(AnnotationItem item, Vector delta) =>
        item switch
        {
            RectAnnotation rect => rect with { Bounds = TranslateRect(rect.Bounds, delta) },
            EllipseAnnotation ellipse => ellipse with { Bounds = TranslateRect(ellipse.Bounds, delta) },
            ArrowAnnotation arrow => arrow with
            {
                Start = arrow.Start + delta,
                End = arrow.End + delta
            },
            PathAnnotation path => path with
            {
                Points = path.Points.Select(point => point + delta).ToArray()
            },
            TextAnnotation text => text with { Position = text.Position + delta },
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unsupported annotation type.")
        };

    private static Rect TranslateRect(Rect rect, Vector delta) =>
        new(rect.X + delta.X, rect.Y + delta.Y, rect.Width, rect.Height);
}
