using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using TextBox = System.Windows.Controls.TextBox;

namespace ScreenshotTool.Editor;

public sealed class AnnotationCanvas : Canvas
{
    private const double MinimumDragDistance = 2;

    private readonly List<AnnotationItem> _items = [];
    private readonly UndoStack _undoStack = new();

    private ToolKind _currentTool;
    private AnnotationItem? _selectedItem;
    private AnnotationItem? _previewItem;
    private TextBox? _activeTextBox;
    private List<Point>? _workingPoints;
    private Point _dragStartPoint;
    private AnnotationItem? _moveSourceItem;
    private Int32Rect? _previewMosaicRect;
    private bool _isDrawing;
    private bool _isMovingSelection;

    public AnnotationCanvas(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        BaseImage = image as WriteableBitmap ?? new WriteableBitmap(image);

        Width = BaseImage.PixelWidth;
        Height = BaseImage.PixelHeight;
        ClipToBounds = true;
        Focusable = true;
        Background = Brushes.Transparent;
        Cursor = Cursors.Cross;
    }

    public event EventHandler? StateChanged;

    public event EventHandler? ContentChanged;

    public WriteableBitmap BaseImage { get; }

    public IReadOnlyList<AnnotationItem> Annotations => _items;

    public ToolKind CurrentTool
    {
        get => _currentTool;
        set
        {
            if (_currentTool == value)
            {
                return;
            }

            CommitActiveText();
            _currentTool = value;
            ClearTransientState();
            Cursor = value switch
            {
                ToolKind.Select => Cursors.Arrow,
                ToolKind.Text => Cursors.IBeam,
                _ => Cursors.Cross
            };

            RaiseStateChanged();
            InvalidateVisual();
        }
    }

    public Color StrokeColor { get; set; } = Colors.Red;

    public double StrokeThickness { get; set; } = 4;

    public double TextFontSize { get; set; } = 24;

    public bool CanUndo => _undoStack.CanUndo;

    public bool CanRedo => _undoStack.CanRedo;

    public bool HasSelection => _selectedItem is not null;

    public void Undo()
    {
        CommitActiveText();
        ClearTransientState();
        _undoStack.Undo();
        SyncSelectionAfterHistory();
        RaiseContentChanged();
        InvalidateVisual();
    }

    public void Redo()
    {
        CommitActiveText();
        ClearTransientState();
        _undoStack.Redo();
        SyncSelectionAfterHistory();
        RaiseContentChanged();
        InvalidateVisual();
    }

    public bool DeleteSelection()
    {
        CommitActiveText();
        if (_selectedItem is null)
        {
            return false;
        }

        ExecuteCommand(new RemoveAnnotationCommand(_items, _selectedItem), selectedItem: null);
        return true;
    }

    public void CommitPendingEdits() => CommitActiveText();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var point = ClampPoint(e.GetPosition(this));

        if (CurrentTool != ToolKind.Text)
        {
            CommitActiveText();
        }

        switch (CurrentTool)
        {
            case ToolKind.Text:
                BeginTextEdit(point);
                e.Handled = true;
                return;

            case ToolKind.Select:
                HandleSelectionMouseDown(point);
                e.Handled = true;
                return;

            case ToolKind.Rectangle:
            case ToolKind.Ellipse:
            case ToolKind.Arrow:
            case ToolKind.Mosaic:
                _dragStartPoint = point;
                _isDrawing = true;
                CaptureMouse();
                e.Handled = true;
                return;

            case ToolKind.Pen:
            case ToolKind.Highlighter:
                _dragStartPoint = point;
                _isDrawing = true;
                _workingPoints = [point];
                _previewItem = CreatePathPreview(_workingPoints);
                CaptureMouse();
                InvalidateVisual();
                e.Handled = true;
                return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var point = ClampPoint(e.GetPosition(this));
        if (_isMovingSelection && _moveSourceItem is not null)
        {
            var delta = point - _dragStartPoint;
            _previewItem = TranslateAnnotation(_moveSourceItem, delta);
            InvalidateVisual();
            return;
        }

        if (!_isDrawing)
        {
            return;
        }

        switch (CurrentTool)
        {
            case ToolKind.Rectangle:
                _previewItem = new RectAnnotation(Guid.NewGuid(), CreateRect(_dragStartPoint, point), StrokeColor, StrokeThickness);
                break;
            case ToolKind.Ellipse:
                _previewItem = new EllipseAnnotation(Guid.NewGuid(), CreateRect(_dragStartPoint, point), StrokeColor, StrokeThickness);
                break;
            case ToolKind.Arrow:
                _previewItem = new ArrowAnnotation(Guid.NewGuid(), _dragStartPoint, point, StrokeColor, StrokeThickness);
                break;
            case ToolKind.Pen:
            case ToolKind.Highlighter:
                if (_workingPoints is not null && ShouldAppendPoint(_workingPoints, point))
                {
                    _workingPoints.Add(point);
                    _previewItem = CreatePathPreview(_workingPoints);
                }

                break;
            case ToolKind.Mosaic:
                _previewMosaicRect = ToPixelRect(CreateRect(_dragStartPoint, point));
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var point = ClampPoint(e.GetPosition(this));
        ReleaseMouseCapture();

        if (_isMovingSelection && _moveSourceItem is not null)
        {
            var delta = point - _dragStartPoint;
            _isMovingSelection = false;
            _previewItem = null;

            if (delta.Length >= MinimumDragDistance && TryGetItemIndex(_moveSourceItem, out var selectedIndex))
            {
                var movedItem = TranslateAnnotation(_moveSourceItem, delta);
                ExecuteCommand(new MoveAnnotationCommand(_items, selectedIndex, delta), movedItem);
            }
            else
            {
                InvalidateVisual();
                RaiseStateChanged();
            }

            _moveSourceItem = null;
            return;
        }

        if (!_isDrawing)
        {
            return;
        }

        _isDrawing = false;

        switch (CurrentTool)
        {
            case ToolKind.Rectangle:
                CommitShape(point, rect => new RectAnnotation(Guid.NewGuid(), rect, StrokeColor, StrokeThickness));
                break;
            case ToolKind.Ellipse:
                CommitShape(point, rect => new EllipseAnnotation(Guid.NewGuid(), rect, StrokeColor, StrokeThickness));
                break;
            case ToolKind.Arrow:
                if ((point - _dragStartPoint).Length >= MinimumDragDistance)
                {
                    var annotation = new ArrowAnnotation(Guid.NewGuid(), _dragStartPoint, point, StrokeColor, StrokeThickness);
                    ExecuteCommand(new AddAnnotationCommand(_items, annotation), annotation);
                }
                else
                {
                    ClearTransientState();
                    RaiseStateChanged();
                    InvalidateVisual();
                }

                break;
            case ToolKind.Pen:
            case ToolKind.Highlighter:
                CommitPath();
                break;
            case ToolKind.Mosaic:
                CommitMosaic();
                break;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        dc.DrawImage(BaseImage, new Rect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight));
        ImageExportService.DrawAnnotations(dc, _items);

        if (_previewItem is not null)
        {
            ImageExportService.DrawAnnotation(dc, _previewItem);
        }

        if (_previewMosaicRect is { Width: > 0, Height: > 0 } mosaicRect)
        {
            var previewRect = new Rect(mosaicRect.X, mosaicRect.Y, mosaicRect.Width, mosaicRect.Height);
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(80, 0, 122, 204)),
                new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dash },
                previewRect);
        }

        if (_selectedItem is not null && _previewItem is null && _activeTextBox is null)
        {
            var bounds = ImageExportService.GetBounds(_selectedItem);
            bounds.Inflate(4, 4);
            dc.DrawRectangle(
                null,
                new Pen(Brushes.DeepSkyBlue, 1.5) { DashStyle = DashStyles.Dash },
                bounds);
        }
    }

    private void HandleSelectionMouseDown(Point point)
    {
        CommitActiveText();

        if (TryHitTest(point, out var item))
        {
            _selectedItem = item;
            _moveSourceItem = item;
            _dragStartPoint = point;
            _isMovingSelection = true;
            CaptureMouse();
        }
        else
        {
            _selectedItem = null;
            _moveSourceItem = null;
            _previewItem = null;
        }

        RaiseStateChanged();
        InvalidateVisual();
    }

    private void BeginTextEdit(Point point)
    {
        CommitActiveText();

        var textBox = new TextBox
        {
            MinWidth = 140,
            FontSize = TextFontSize,
            Foreground = new SolidColorBrush(StrokeColor),
            Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(StrokeColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            AcceptsReturn = false
        };

        textBox.KeyDown += ActiveTextBox_KeyDown;
        textBox.LostKeyboardFocus += ActiveTextBox_LostKeyboardFocus;

        SetLeft(textBox, point.X);
        SetTop(textBox, point.Y);
        Children.Add(textBox);
        _activeTextBox = textBox;
        textBox.Focus();
        textBox.SelectAll();
        RaiseStateChanged();
    }

    private void ActiveTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitActiveText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RemoveActiveTextBox();
            e.Handled = true;
        }
    }

    private void ActiveTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitActiveText();

    private void CommitActiveText()
    {
        if (_activeTextBox is null)
        {
            return;
        }

        var textBox = _activeTextBox;
        var left = GetLeft(textBox);
        var top = GetTop(textBox);
        var text = textBox.Text.Trim();

        RemoveActiveTextBox();

        if (string.IsNullOrWhiteSpace(text))
        {
            InvalidateVisual();
            RaiseStateChanged();
            return;
        }

        var annotation = new TextAnnotation(Guid.NewGuid(), new Point(left, top), text, StrokeColor, TextFontSize);
        ExecuteCommand(new AddAnnotationCommand(_items, annotation), annotation);
    }

    private void RemoveActiveTextBox()
    {
        if (_activeTextBox is null)
        {
            return;
        }

        _activeTextBox.KeyDown -= ActiveTextBox_KeyDown;
        _activeTextBox.LostKeyboardFocus -= ActiveTextBox_LostKeyboardFocus;
        Children.Remove(_activeTextBox);
        _activeTextBox = null;
        RaiseStateChanged();
    }

    private void CommitShape(Point point, Func<Rect, AnnotationItem> createAnnotation)
    {
        var rect = CreateRect(_dragStartPoint, point);
        ClearTransientState();

        if (rect.Width < MinimumDragDistance || rect.Height < MinimumDragDistance)
        {
            RaiseStateChanged();
            InvalidateVisual();
            return;
        }

        var annotation = createAnnotation(rect);
        ExecuteCommand(new AddAnnotationCommand(_items, annotation), annotation);
    }

    private void CommitPath()
    {
        var points = _workingPoints?.ToArray() ?? [];
        ClearTransientState();

        if (points.Length == 0)
        {
            RaiseStateChanged();
            InvalidateVisual();
            return;
        }

        var annotation = new PathAnnotation(
            Guid.NewGuid(),
            points,
            StrokeColor,
            StrokeThickness,
            CurrentTool == ToolKind.Highlighter);

        ExecuteCommand(new AddAnnotationCommand(_items, annotation), annotation);
    }

    private void CommitMosaic()
    {
        var rect = _previewMosaicRect;
        ClearTransientState();

        if (rect is not { Width: > 0, Height: > 0 } mosaicRect)
        {
            RaiseStateChanged();
            InvalidateVisual();
            return;
        }

        var blockSize = Math.Max(6, (int)Math.Round(StrokeThickness * 2));
        ExecuteCommand(new MosaicCommand(BaseImage, mosaicRect, blockSize), selectedItem: null);
    }

    private void ExecuteCommand(IAnnotationCommand command, AnnotationItem? selectedItem)
    {
        _undoStack.Execute(command);
        _selectedItem = selectedItem;
        _moveSourceItem = null;
        _previewItem = null;
        _previewMosaicRect = null;
        RaiseContentChanged();
        InvalidateVisual();
    }

    private void SyncSelectionAfterHistory()
    {
        if (_selectedItem is null)
        {
            RaiseStateChanged();
            return;
        }

        _selectedItem = _items.FirstOrDefault(item => item.Id == _selectedItem.Id);
        RaiseStateChanged();
    }

    private void ClearTransientState()
    {
        _previewItem = null;
        _previewMosaicRect = null;
        _workingPoints = null;
        _isDrawing = false;
        _isMovingSelection = false;
        _moveSourceItem = null;
    }

    private bool TryHitTest(Point point, out AnnotationItem item)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (ContainsPoint(_items[i], point))
            {
                item = _items[i];
                return true;
            }
        }

        item = null!;
        return false;
    }

    private static bool ContainsPoint(AnnotationItem item, Point point)
    {
        if (item is TextAnnotation text)
        {
            return ImageExportService.GetBounds(text).Contains(point);
        }

        var geometry = ImageExportService.CreateGeometry(item);
        if (geometry.FillContains(point))
        {
            return true;
        }

        var pen = item switch
        {
            RectAnnotation rect => ImageExportService.CreatePen(rect.StrokeColor, Math.Max(6, rect.StrokeThickness)),
            EllipseAnnotation ellipse => ImageExportService.CreatePen(ellipse.StrokeColor, Math.Max(6, ellipse.StrokeThickness)),
            ArrowAnnotation arrow => ImageExportService.CreatePen(arrow.StrokeColor, Math.Max(6, arrow.StrokeThickness)),
            PathAnnotation path => ImageExportService.CreatePen(path.StrokeColor, Math.Max(8, path.StrokeThickness), path.IsHighlighter),
            _ => null
        };

        return pen is not null && geometry.StrokeContains(pen, point);
    }

    private bool TryGetItemIndex(AnnotationItem item, out int index)
    {
        index = _items.FindIndex(candidate => candidate.Id == item.Id);
        return index >= 0;
    }

    private PathAnnotation CreatePathPreview(IReadOnlyList<Point> points) =>
        new(
            Guid.NewGuid(),
            points,
            StrokeColor,
            StrokeThickness,
            CurrentTool == ToolKind.Highlighter);

    private AnnotationItem TranslateAnnotation(AnnotationItem item, Vector delta) =>
        item switch
        {
            RectAnnotation rect => rect with { Bounds = TranslateRect(rect.Bounds, delta) },
            EllipseAnnotation ellipse => ellipse with { Bounds = TranslateRect(ellipse.Bounds, delta) },
            ArrowAnnotation arrow => arrow with { Start = arrow.Start + delta, End = arrow.End + delta },
            PathAnnotation path => path with { Points = path.Points.Select(point => point + delta).ToArray() },
            TextAnnotation text => text with { Position = text.Position + delta },
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unsupported annotation type.")
        };

    private static Rect TranslateRect(Rect rect, Vector delta) =>
        new(rect.X + delta.X, rect.Y + delta.Y, rect.Width, rect.Height);

    private static Rect CreateRect(Point start, Point end) => new(start, end);

    private static bool ShouldAppendPoint(IReadOnlyList<Point> points, Point candidate)
    {
        if (points.Count == 0)
        {
            return true;
        }

        return (candidate - points[^1]).Length >= 0.8;
    }

    private Int32Rect ToPixelRect(Rect rect)
    {
        var normalized = rect;
        normalized = Rect.Intersect(normalized, new Rect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight));
        if (normalized.IsEmpty)
        {
            return Int32Rect.Empty;
        }

        var x = (int)Math.Floor(normalized.X);
        var y = (int)Math.Floor(normalized.Y);
        var width = (int)Math.Ceiling(normalized.Width);
        var height = (int)Math.Ceiling(normalized.Height);
        return new Int32Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    private Point ClampPoint(Point point) =>
        new(
            Math.Clamp(point.X, 0, BaseImage.PixelWidth),
            Math.Clamp(point.Y, 0, BaseImage.PixelHeight));

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseContentChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
}
