using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Native;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace ScreenshotTool.Capture;

public partial class CaptureOverlayWindow : Window
{
    private readonly BitmapSource _screenBitmap;
    private readonly ScreenCaptureService.VirtualScreenInfo _virtualScreen;
    private readonly List<Point> _lassoPoints = [];

    private CaptureMode _mode = CaptureMode.Rectangle;
    private CaptureState _state = CaptureState.Idle;
    private Point _dragStartPixel;
    private Int32Rect _selectionBounds = Int32Rect.Empty;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private bool _completionRaised;

    public event Action<BitmapSource>? CaptureConfirmed;
    public event Action? CaptureCancelled;

    private CaptureOverlayWindow(BitmapSource screenBitmap, ScreenCaptureService.VirtualScreenInfo virtualScreen)
    {
        ArgumentNullException.ThrowIfNull(screenBitmap);

        InitializeComponent();

        _screenBitmap = screenBitmap;
        _virtualScreen = virtualScreen;

        Left = virtualScreen.X;
        Top = virtualScreen.Y;
        Width = virtualScreen.Width;
        Height = virtualScreen.Height;

        BackgroundImage.Source = screenBitmap;

        Loaded += Window_Loaded;
        SourceInitialized += Window_SourceInitialized;
        Closed += Window_Closed;
    }

    public static CaptureOverlayWindow ShowNew()
    {
        var screen = ScreenCaptureService.CaptureVirtualScreen();
        var vs = ScreenCaptureService.GetVirtualScreen();
        var window = new CaptureOverlayWindow(screen, vs);
        window.Show();
        window.Activate();
        window.Focus();
        return window;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(
            handle,
            Win32.HWND_TOPMOST,
            _virtualScreen.X,
            _virtualScreen.Y,
            _virtualScreen.Width,
            _virtualScreen.Height,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Cross;
        UpdateModeButtons();
        UpdateScaleFactors();
        RenderSelectionState();
        Focus();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        Mouse.OverrideCursor = null;

        if (_completionRaised)
        {
            return;
        }

        _completionRaised = true;
        CaptureCancelled?.Invoke();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.R:
                SetMode(CaptureMode.Rectangle);
                e.Handled = true;
                break;
            case Key.F:
                SetMode(CaptureMode.Lasso);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_state == CaptureState.PendingConfirm)
                {
                    ConfirmSelection();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                CancelCapture();
                e.Handled = true;
                break;
        }
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CancelCapture();
        e.Handled = true;
    }

    private void RectangleModeButton_Click(object sender, RoutedEventArgs e) => SetMode(CaptureMode.Rectangle);

    private void LassoModeButton_Click(object sender, RoutedEventArgs e) => SetMode(CaptureMode.Lasso);

    private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state == CaptureState.PendingConfirm || SelectionCanvas.ActualWidth <= 0 || SelectionCanvas.ActualHeight <= 0)
        {
            return;
        }

        Focus();
        _state = CaptureState.Drawing;
        _selectionBounds = Int32Rect.Empty;
        _lassoPoints.Clear();
        _dragStartPixel = ViewToPixel(e.GetPosition(SelectionCanvas));

        if (_mode == CaptureMode.Rectangle)
        {
            _selectionBounds = CaptureOverlaySelection.NormalizeRectangle(_dragStartPixel, _dragStartPixel);
        }
        else
        {
            _lassoPoints.Add(_dragStartPixel);
        }

        SelectionCanvas.CaptureMouse();
        RenderSelectionState();
        e.Handled = true;
    }

    private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_state != CaptureState.Drawing)
        {
            return;
        }

        var pixelPoint = ViewToPixel(e.GetPosition(SelectionCanvas));
        if (_mode == CaptureMode.Rectangle)
        {
            _selectionBounds = CaptureOverlaySelection.NormalizeRectangle(_dragStartPixel, pixelPoint);
        }
        else if (ShouldAppendLassoPoint(pixelPoint))
        {
            _lassoPoints.Add(pixelPoint);
            _selectionBounds = RegionGeometry.GetBoundingRect(_lassoPoints);
        }

        RenderSelectionState();
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_state != CaptureState.Drawing)
        {
            return;
        }

        SelectionCanvas.ReleaseMouseCapture();

        var pixelPoint = ViewToPixel(e.GetPosition(SelectionCanvas));
        if (_mode == CaptureMode.Rectangle)
        {
            _selectionBounds = CaptureOverlaySelection.NormalizeRectangle(_dragStartPixel, pixelPoint);
            if (!CaptureOverlaySelection.IsValidRectangle(_selectionBounds))
            {
                ResetSelection();
                return;
            }
        }
        else
        {
            if (ShouldAppendLassoPoint(pixelPoint))
            {
                _lassoPoints.Add(pixelPoint);
            }

            if (!CaptureOverlaySelection.IsValidLasso(_lassoPoints))
            {
                ResetSelection();
                return;
            }

            _selectionBounds = RegionGeometry.GetBoundingRect(_lassoPoints);
        }

        _state = CaptureState.PendingConfirm;
        RenderSelectionState();
        e.Handled = true;
    }

    private void SelectionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateScaleFactors();
        RenderSelectionState();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => ConfirmSelection();

    private void ReselectButton_Click(object sender, RoutedEventArgs e) => ResetSelection();

    private void SetMode(CaptureMode mode)
    {
        if (_mode == mode && _state == CaptureState.Idle)
        {
            return;
        }

        _mode = mode;
        ResetSelection();
        UpdateModeButtons();
    }

    private void ResetSelection()
    {
        SelectionCanvas.ReleaseMouseCapture();
        _state = CaptureState.Idle;
        _selectionBounds = Int32Rect.Empty;
        _lassoPoints.Clear();
        RenderSelectionState();
    }

    private void CancelCapture()
    {
        SelectionCanvas.ReleaseMouseCapture();
        _completionRaised = true;
        CaptureCancelled?.Invoke();
        Close();
    }

    private void ConfirmSelection()
    {
        if (_state != CaptureState.PendingConfirm)
        {
            return;
        }

        BitmapSource? capture = _mode switch
        {
            CaptureMode.Rectangle => CaptureOverlaySelection.IsValidRectangle(_selectionBounds)
                ? RegionGeometry.CropRectangle(_screenBitmap, _selectionBounds)
                : null,
            CaptureMode.Lasso => RegionGeometry.CropLasso(_screenBitmap, _lassoPoints),
            _ => null
        };

        if (capture is not null && capture.CanFreeze)
        {
            capture.Freeze();
        }

        if (capture is null)
        {
            ResetSelection();
            return;
        }

        _completionRaised = true;
        CaptureConfirmed?.Invoke(capture);
        Close();
    }

    private void UpdateModeButtons()
    {
        ApplyModeButtonStyle(RectangleModeButton, _mode == CaptureMode.Rectangle);
        ApplyModeButtonStyle(LassoModeButton, _mode == CaptureMode.Lasso);
    }

    private static void ApplyModeButtonStyle(System.Windows.Controls.Button button, bool isActive)
    {
        button.Background = isActive
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        button.Foreground = System.Windows.Media.Brushes.White;
        button.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
    }

    private void UpdateScaleFactors()
    {
        if (SelectionCanvas.ActualWidth <= 0 || SelectionCanvas.ActualHeight <= 0)
        {
            return;
        }

        _scaleX = _screenBitmap.PixelWidth / SelectionCanvas.ActualWidth;
        _scaleY = _screenBitmap.PixelHeight / SelectionCanvas.ActualHeight;
    }

    private void RenderSelectionState()
    {
        if (SelectionCanvas.ActualWidth <= 0 || SelectionCanvas.ActualHeight <= 0)
        {
            return;
        }

        Geometry? selectionGeometry = null;
        var showPreview = false;

        switch (_mode)
        {
            case CaptureMode.Rectangle:
                if (CaptureOverlaySelection.IsValidRectangle(_selectionBounds))
                {
                    selectionGeometry = new RectangleGeometry(PixelRectToViewRect(_selectionBounds));
                }

                break;

            case CaptureMode.Lasso:
                if (_state == CaptureState.PendingConfirm && CaptureOverlaySelection.IsValidLasso(_lassoPoints))
                {
                    selectionGeometry = CreateLassoGeometry(isClosed: true);
                }
                else if (_state == CaptureState.Drawing && _lassoPoints.Count >= 2)
                {
                    showPreview = true;
                }

                break;
        }

        UpdateOverlay(selectionGeometry);
        SelectionShape.Data = selectionGeometry;
        SelectionShape.Visibility = selectionGeometry is null ? Visibility.Collapsed : Visibility.Visible;

        if (showPreview)
        {
            var previewPoints = new PointCollection(_lassoPoints.Select(PixelToView));
            LassoPreview.Points = previewPoints;
            LassoPreview.Visibility = Visibility.Visible;
        }
        else
        {
            LassoPreview.Points = [];
            LassoPreview.Visibility = Visibility.Collapsed;
        }

        var showInfo = _state != CaptureState.Idle && CaptureOverlaySelection.IsValidRectangle(_selectionBounds);
        SelectionInfoBadge.Visibility = showInfo ? Visibility.Visible : Visibility.Collapsed;
        ConfirmBar.Visibility = _state == CaptureState.PendingConfirm ? Visibility.Visible : Visibility.Collapsed;

        if (showInfo)
        {
            var sizeText = $"{_selectionBounds.Width} x {_selectionBounds.Height}";
            SelectionInfoText.Text = sizeText;
            ConfirmInfoText.Text = sizeText;
            PositionSelectionInfoBadge();
        }
        else
        {
            SelectionInfoText.Text = string.Empty;
            ConfirmInfoText.Text = string.Empty;
        }

        if (_state == CaptureState.PendingConfirm)
        {
            PositionConfirmBar();
        }
    }

    private void UpdateOverlay(Geometry? selectionGeometry)
    {
        var overlay = new GeometryGroup { FillRule = FillRule.EvenOdd };
        overlay.Children.Add(new RectangleGeometry(new Rect(0, 0, SelectionCanvas.ActualWidth, SelectionCanvas.ActualHeight)));

        if (selectionGeometry is not null)
        {
            overlay.Children.Add(selectionGeometry);
        }

        OverlayPath.Data = overlay;
    }

    private void PositionSelectionInfoBadge()
    {
        SelectionInfoBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var badgeSize = SelectionInfoBadge.DesiredSize;
        var selectionRect = PixelRectToViewRect(_selectionBounds);

        var x = Math.Clamp(
            selectionRect.X,
            12,
            Math.Max(12, SelectionCanvas.ActualWidth - badgeSize.Width - 12));

        var y = selectionRect.Y - badgeSize.Height - 12;
        if (y < 12)
        {
            y = Math.Min(
                SelectionCanvas.ActualHeight - badgeSize.Height - 12,
                selectionRect.Y + 12);
        }

        System.Windows.Controls.Canvas.SetLeft(SelectionInfoBadge, x);
        System.Windows.Controls.Canvas.SetTop(SelectionInfoBadge, Math.Max(12, y));
    }

    private void PositionConfirmBar()
    {
        ConfirmBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var confirmSize = ConfirmBar.DesiredSize;
        var selectionRect = PixelRectToViewRect(_selectionBounds);
        var viewBounds = new Int32Rect(
            (int)Math.Round(selectionRect.X),
            (int)Math.Round(selectionRect.Y),
            (int)Math.Round(selectionRect.Width),
            (int)Math.Round(selectionRect.Height));

        var position = CaptureOverlaySelection.GetConfirmationBarPosition(
            viewBounds,
            confirmSize,
            new Size(SelectionCanvas.ActualWidth, SelectionCanvas.ActualHeight));

        System.Windows.Controls.Canvas.SetLeft(ConfirmBar, position.X);
        System.Windows.Controls.Canvas.SetTop(ConfirmBar, position.Y);
    }

    private Geometry CreateLassoGeometry(bool isClosed)
    {
        var geometry = new StreamGeometry();

        using var context = geometry.Open();
        context.BeginFigure(PixelToView(_lassoPoints[0]), isFilled: isClosed, isClosed: isClosed);

        for (var i = 1; i < _lassoPoints.Count; i++)
        {
            context.LineTo(PixelToView(_lassoPoints[i]), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    private bool ShouldAppendLassoPoint(Point pixelPoint)
    {
        if (_lassoPoints.Count == 0)
        {
            return true;
        }

        var previous = _lassoPoints[^1];
        var deltaX = previous.X - pixelPoint.X;
        var deltaY = previous.Y - pixelPoint.Y;
        return deltaX * deltaX + deltaY * deltaY >= 1;
    }

    private Point ViewToPixel(Point point)
    {
        var x = Math.Clamp(point.X * _scaleX, 0, _screenBitmap.PixelWidth);
        var y = Math.Clamp(point.Y * _scaleY, 0, _screenBitmap.PixelHeight);
        return new Point(x, y);
    }

    private Point PixelToView(Point point)
    {
        var x = _scaleX == 0 ? 0 : point.X / _scaleX;
        var y = _scaleY == 0 ? 0 : point.Y / _scaleY;
        return new Point(x, y);
    }

    private Rect PixelRectToViewRect(Int32Rect rect)
    {
        var topLeft = PixelToView(new Point(rect.X, rect.Y));
        var bottomRight = PixelToView(new Point(rect.X + rect.Width, rect.Y + rect.Height));
        return new Rect(topLeft, bottomRight);
    }

    private enum CaptureMode
    {
        Rectangle,
        Lasso
    }

    private enum CaptureState
    {
        Idle,
        Drawing,
        PendingConfirm
    }
}
