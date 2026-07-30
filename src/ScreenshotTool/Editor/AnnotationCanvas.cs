using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
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
    private readonly Image _backgroundImage;
    private readonly AnnotationLayer _annotationLayer;

    private ToolKind _currentTool;
    private AnnotationItem? _selectedItem;
    private AnnotationItem? _hoverItem;
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

        BaseImage = CreateMutableBitmap(image);

        Width = BaseImage.PixelWidth;
        Height = BaseImage.PixelHeight;
        ClipToBounds = true;
        Focusable = true;
        Background = Brushes.Transparent;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);

        _backgroundImage = new Image
        {
            Source = BaseImage,
            Width = BaseImage.PixelWidth,
            Height = BaseImage.PixelHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_backgroundImage, BitmapScalingMode.NearestNeighbor);
        SetLeft(_backgroundImage, 0);
        SetTop(_backgroundImage, 0);
        SetZIndex(_backgroundImage, 0);

        _annotationLayer = new AnnotationLayer(this)
        {
            Width = BaseImage.PixelWidth,
            Height = BaseImage.PixelHeight,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        SetLeft(_annotationLayer, 0);
        SetTop(_annotationLayer, 0);
        SetZIndex(_annotationLayer, 1);

        Children.Add(_backgroundImage);
        Children.Add(_annotationLayer);

        Loaded += (_, _) => ApplyDpiScaling();
    }

    public event EventHandler? StateChanged;

    public event EventHandler? ContentChanged;

    public WriteableBitmap BaseImage { get; }

    public IReadOnlyList<AnnotationItem> Annotations => _items;

    public AnnotationItem? SelectedAnnotation => _selectedItem;

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
            _hoverItem = null;
            Cursor = value switch
            {
                ToolKind.Select => Cursors.Arrow,
                ToolKind.Text => Cursors.IBeam,
                _ => Cursors.Cross
            };

            RaiseStateChanged();
            InvalidateAnnotations();
        }
    }

    public Color StrokeColor { get; set; } = Colors.Red;

    public double StrokeThickness { get; set; } = 4;

    public double TextFontSize { get; set; } = 24;

    public bool CanUndo => _undoStack.CanUndo;

    public bool CanRedo => _undoStack.CanRedo;

    public bool HasSelection => _selectedItem is not null;

    public bool Undo()
    {
        CommitActiveText();
        ClearTransientState();
        if (!_undoStack.Undo())
        {
            return false;
        }

        SyncSelectionAfterHistory();
        RefreshBackground();
        RaiseContentChanged();
        InvalidateAnnotations();
        return true;
    }

    public bool Redo()
    {
        CommitActiveText();
        ClearTransientState();
        if (!_undoStack.Redo())
        {
            return false;
        }

        SyncSelectionAfterHistory();
        RefreshBackground();
        RaiseContentChanged();
        InvalidateAnnotations();
        return true;
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

    public void SyncToolbarFromSelection(Action<Color?, double?, double?> apply)
    {
        switch (_selectedItem)
        {
            case RectAnnotation rect:
                apply(rect.StrokeColor, rect.StrokeThickness, null);
                break;
            case EllipseAnnotation ellipse:
                apply(ellipse.StrokeColor, ellipse.StrokeThickness, null);
                break;
            case ArrowAnnotation arrow:
                apply(arrow.StrokeColor, arrow.StrokeThickness, null);
                break;
            case PathAnnotation path:
                apply(path.StrokeColor, path.StrokeThickness, null);
                break;
            case TextAnnotation text:
                apply(text.TextColor, null, text.FontSize);
                break;
            default:
                apply(null, null, null);
                break;
        }
    }

    public bool ApplyColorToSelection(Color color) =>
        TryApplyStyleToSelection(color: color, thickness: null, fontSize: null);

    public bool ApplyThicknessToSelection(double thickness) =>
        TryApplyStyleToSelection(color: null, thickness: thickness, fontSize: null);

    public bool ApplyFontSizeToSelection(double fontSize) =>
        TryApplyStyleToSelection(color: null, thickness: null, fontSize: fontSize);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyDpiScaling(newDpi);
    }

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
                InvalidateAnnotations();
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
            InvalidateAnnotations();
            return;
        }

        if (_isDrawing)
        {
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

            InvalidateAnnotations();
            return;
        }

        if (CurrentTool == ToolKind.Select && e.LeftButton == MouseButtonState.Released)
        {
            UpdateHover(point);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverItem is not null && !_isMovingSelection && !_isDrawing)
        {
            _hoverItem = null;
            if (CurrentTool == ToolKind.Select)
            {
                Cursor = Cursors.Arrow;
            }

            InvalidateAnnotations();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var point = ClampPoint(e.GetPosition(this));

        // Finish the gesture BEFORE releasing capture. ReleaseMouseCapture synchronously
        // raises LostMouseCapture, which would otherwise clear drawing state and skip commit.
        if (_isMovingSelection && _moveSourceItem is not null)
        {
            var delta = point - _dragStartPoint;
            var source = _moveSourceItem;
            _isMovingSelection = false;
            _previewItem = null;
            _moveSourceItem = null;
            if (CurrentTool == ToolKind.Select)
            {
                Cursor = Cursors.Arrow;
            }

            if (delta.Length >= MinimumDragDistance && TryGetItemIndex(source, out var selectedIndex))
            {
                var movedItem = TranslateAnnotation(source, delta);
                ExecuteCommand(new MoveAnnotationCommand(_items, selectedIndex, delta), movedItem);
            }
            else
            {
                InvalidateAnnotations();
                RaiseStateChanged();
            }

            ReleaseMouseCapture();
            UpdateHover(point);
            e.Handled = true;
            return;
        }

        if (_isDrawing)
        {
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
                        InvalidateAnnotations();
                    }

                    break;
                case ToolKind.Pen:
                case ToolKind.Highlighter:
                    CommitPath();
                    break;
                case ToolKind.Mosaic:
                    // Recompute from the release point so we don't rely solely on last MouseMove.
                    _previewMosaicRect = ToPixelRect(CreateRect(_dragStartPoint, point));
                    CommitMosaic();
                    break;
            }

            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        // Only runs for interrupted capture (focus loss, etc.). Normal mouse-up finishes above first.
        if (_isMovingSelection)
        {
            _isMovingSelection = false;
            _previewItem = null;
            _moveSourceItem = null;
            if (CurrentTool == ToolKind.Select)
            {
                Cursor = Cursors.Arrow;
            }

            InvalidateAnnotations();
            RaiseStateChanged();
            return;
        }

        if (_isDrawing)
        {
            ClearTransientState();
            InvalidateAnnotations();
            RaiseStateChanged();
        }
    }

    private void RenderAnnotations(DrawingContext dc)
    {
        var excludeId = _isMovingSelection && _moveSourceItem is not null
            ? _moveSourceItem.Id
            : (Guid?)null;
        ImageExportService.DrawAnnotations(dc, _items, excludeId);

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

        if (_hoverItem is not null &&
            !_isMovingSelection &&
            _activeTextBox is null &&
            (_selectedItem is null || _hoverItem.Id != _selectedItem.Id))
        {
            var hoverBounds = ImageExportService.GetBounds(_hoverItem);
            hoverBounds.Inflate(3, 3);
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(28, 255, 165, 0)),
                new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 140, 0)), 1) { DashStyle = DashStyles.Dot },
                hoverBounds);
        }

        var selectionItem = _isMovingSelection && _previewItem is not null
            ? _previewItem
            : _selectedItem is not null && _previewItem is null && _activeTextBox is null
                ? _selectedItem
                : null;

        if (selectionItem is not null)
        {
            var bounds = ImageExportService.GetBounds(selectionItem);
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
            _hoverItem = item;
            _dragStartPoint = point;
            _isMovingSelection = true;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
        else
        {
            _selectedItem = null;
            _moveSourceItem = null;
            _previewItem = null;
            _hoverItem = null;
            Cursor = Cursors.Arrow;
        }

        RaiseStateChanged();
        InvalidateAnnotations();
    }

    private void UpdateHover(Point point)
    {
        AnnotationItem? hover = null;
        if (TryHitTest(point, out var item))
        {
            hover = item;
        }

        if (ReferenceEquals(hover, _hoverItem) ||
            (hover is not null && _hoverItem is not null && hover.Id == _hoverItem.Id))
        {
            if (hover is not null)
            {
                Cursor = Cursors.SizeAll;
            }
            else if (CurrentTool == ToolKind.Select)
            {
                Cursor = Cursors.Arrow;
            }

            return;
        }

        _hoverItem = hover;
        Cursor = hover is not null ? Cursors.SizeAll : Cursors.Arrow;
        InvalidateAnnotations();
    }

    private bool TryApplyStyleToSelection(Color? color, double? thickness, double? fontSize)
    {
        if (_selectedItem is null || !TryGetItemIndex(_selectedItem, out var index))
        {
            return false;
        }

        var updated = ApplyStyle(_selectedItem, color, thickness, fontSize);
        if (Equals(updated, _selectedItem))
        {
            return false;
        }

        ExecuteCommand(new ReplaceAnnotationCommand(_items, index, updated), updated);
        return true;
    }

    private static AnnotationItem ApplyStyle(AnnotationItem item, Color? color, double? thickness, double? fontSize) =>
        item switch
        {
            RectAnnotation rect => rect with
            {
                StrokeColor = color ?? rect.StrokeColor,
                StrokeThickness = thickness ?? rect.StrokeThickness
            },
            EllipseAnnotation ellipse => ellipse with
            {
                StrokeColor = color ?? ellipse.StrokeColor,
                StrokeThickness = thickness ?? ellipse.StrokeThickness
            },
            ArrowAnnotation arrow => arrow with
            {
                StrokeColor = color ?? arrow.StrokeColor,
                StrokeThickness = thickness ?? arrow.StrokeThickness
            },
            PathAnnotation path => path with
            {
                StrokeColor = color ?? path.StrokeColor,
                StrokeThickness = thickness ?? path.StrokeThickness
            },
            TextAnnotation text => text with
            {
                TextColor = color ?? text.TextColor,
                FontSize = fontSize ?? text.FontSize
            },
            _ => item
        };

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
        SetZIndex(textBox, 2);
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
            InvalidateAnnotations();
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
            InvalidateAnnotations();
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
            InvalidateAnnotations();
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
            InvalidateAnnotations();
            return;
        }

        // Prefer a visibly blocky mosaic; thickness only nudges block size slightly.
        var blockSize = Math.Clamp((int)Math.Round(StrokeThickness * 3), 8, 48);
        ExecuteCommand(new MosaicCommand(BaseImage, mosaicRect, blockSize), selectedItem: null);
    }

    private void ExecuteCommand(IAnnotationCommand command, AnnotationItem? selectedItem)
    {
        _undoStack.Execute(command);
        _selectedItem = selectedItem;
        _moveSourceItem = null;
        _previewItem = null;
        _previewMosaicRect = null;
        if (command is MosaicCommand)
        {
            RefreshBackground();
        }

        RaiseContentChanged();
        InvalidateAnnotations();
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
        var normalized = Rect.Intersect(rect, new Rect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight));
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

    private void InvalidateAnnotations() => _annotationLayer.InvalidateVisual();

    private void RefreshBackground()
    {
        // Image often keeps a cached frame of WriteableBitmap; reassign Source to force redraw.
        var source = BaseImage;
        _backgroundImage.Source = null;
        _backgroundImage.Source = source;
    }

    private static WriteableBitmap CreateMutableBitmap(BitmapSource image)
    {
        // Always copy into a fresh Bgra32 buffer. Capture freezes sources, and Image
        // must bind to a bitmap we can WritePixels into for mosaic.
        var converted = image.Format == PixelFormats.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var dpiX = converted.DpiX > 0 ? converted.DpiX : 96;
        var dpiY = converted.DpiY > 0 ? converted.DpiY : 96;
        var writable = new WriteableBitmap(width, height, dpiX, dpiY, PixelFormats.Bgra32, null);
        var stride = width * 4;
        var pixels = new byte[checked(height * stride)];
        converted.CopyPixels(pixels, stride, 0);
        writable.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return writable;
    }

    private void ApplyDpiScaling() => ApplyDpiScaling(VisualTreeHelper.GetDpi(this));

    private void ApplyDpiScaling(DpiScale dpi)
    {
        var scaleX = dpi.DpiScaleX > 0 ? 1.0 / dpi.DpiScaleX : 1.0;
        var scaleY = dpi.DpiScaleY > 0 ? 1.0 / dpi.DpiScaleY : 1.0;
        LayoutTransform = new ScaleTransform(scaleX, scaleY);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseContentChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Transparent overlay that redraws annotations without re-blitting the screenshot bitmap.
    /// </summary>
    private sealed class AnnotationLayer : FrameworkElement
    {
        private readonly AnnotationCanvas _owner;

        public AnnotationLayer(AnnotationCanvas owner)
        {
            _owner = owner;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            _owner.RenderAnnotations(drawingContext);
        }
    }
}
