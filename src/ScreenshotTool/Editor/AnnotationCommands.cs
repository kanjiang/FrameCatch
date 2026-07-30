using System.Windows;
using System.Windows.Media.Imaging;
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

public sealed class ReplaceAnnotationCommand : IAnnotationCommand
{
    private readonly IList<AnnotationItem> _items;
    private readonly int _index;
    private readonly AnnotationItem _original;
    private readonly AnnotationItem _replacement;

    public ReplaceAnnotationCommand(IList<AnnotationItem> items, int index, AnnotationItem replacement)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, items.Count);
        ArgumentNullException.ThrowIfNull(replacement);

        _items = items;
        _index = index;
        _original = items[index];
        _replacement = replacement;
    }

    public void Do() => _items[_index] = _replacement;

    public void Undo() => _items[_index] = _original;
}

public sealed class MosaicCommand : IAnnotationCommand
{
    private readonly WriteableBitmap _bitmap;
    private readonly Int32Rect _rect;
    private readonly int _blockSize;
    private byte[]? _backup;

    public MosaicCommand(WriteableBitmap bitmap, Int32Rect rect, int blockSize = 8)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        _bitmap = bitmap;
        _rect = rect;
        _blockSize = blockSize;
    }

    public void Do()
    {
        _backup ??= MosaicHelper.CapturePixels(_bitmap, _rect);
        MosaicHelper.ApplyMosaic(_bitmap, _rect, _blockSize);
    }

    public void Undo()
    {
        if (_backup is { Length: > 0 })
        {
            MosaicHelper.RestorePixels(_bitmap, _rect, _backup);
        }
    }
}
