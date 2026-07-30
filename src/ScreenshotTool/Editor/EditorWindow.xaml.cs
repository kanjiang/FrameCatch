using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ScreenshotTool.Shell;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Screen = System.Windows.Forms.Screen;
using TextBox = System.Windows.Controls.TextBox;

namespace ScreenshotTool.Editor;

public partial class EditorWindow : Window
{
    private readonly AnnotationCanvas _canvas;
    private readonly string? _defaultSaveDirectory;
    private bool _hasUnexportedChanges;
    private bool _syncingToolbarFromSelection;

    public EditorWindow(BitmapSource image, AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        InitializeComponent();

        _canvas = new AnnotationCanvas(image);
        _canvas.StateChanged += Canvas_StateChanged;
        _canvas.ContentChanged += Canvas_ContentChanged;
        CanvasHost.Child = _canvas;

        if (settings is not null)
        {
            ApplyInitialSettings(settings);
        }

        _defaultSaveDirectory = settings?.DefaultSaveDirectory;

        _canvas.CurrentTool = ToolKind.Select;
        ApplySelectedColor();
        ApplySelectedThickness();
        ApplySelectedFontSize();
        UpdateUiState();
        UpdateTitle();
        SetStatus($"已载入 {_canvas.BaseImage.PixelWidth} x {_canvas.BaseImage.PixelHeight} 图像。");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => FitToCurrentMonitor();

    private void FitToCurrentMonitor()
    {
        var screen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var workArea = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        var workWidthDip = workArea.Width / scaleX;
        var workHeightDip = workArea.Height / scaleY;

        // Keep the window on a single monitor, with a little margin for the taskbar/edges.
        const double occupyRatio = 0.88;
        var maxWidth = Math.Max(MinWidth, workWidthDip * occupyRatio);
        var maxHeight = Math.Max(MinHeight, workHeightDip * occupyRatio);

        // Prefer fitting the screenshot content, but never exceed one screen.
        var imageWidthDip = _canvas.BaseImage.PixelWidth / scaleX;
        var imageHeightDip = _canvas.BaseImage.PixelHeight / scaleY;
        const double chromeWidth = 64;
        const double chromeHeight = 168;

        Width = Math.Clamp(imageWidthDip + chromeWidth, MinWidth, maxWidth);
        Height = Math.Clamp(imageHeightDip + chromeHeight, MinHeight, maxHeight);

        Left = (workArea.Left / scaleX) + ((workWidthDip - Width) / 2);
        Top = (workArea.Top / scaleY) + ((workHeightDip - Height) / 2);
    }

    private void Canvas_StateChanged(object? sender, EventArgs e)
    {
        SyncToolbarFromSelection();
        UpdateUiState();
    }

    private void Canvas_ContentChanged(object? sender, EventArgs e)
    {
        _hasUnexportedChanges = true;
        SyncToolbarFromSelection();
        UpdateUiState();
        UpdateTitle();
    }

    private void ToolButton_Checked(object sender, RoutedEventArgs e) => ApplySelectedTool();

    private void ColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingToolbarFromSelection)
        {
            return;
        }

        ApplySelectedColor();
        if (_canvas.HasSelection && _canvas.ApplyColorToSelection(_canvas.StrokeColor))
        {
            SetStatus("已更新选中标注的颜色。");
        }
    }

    private void ThicknessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingToolbarFromSelection)
        {
            return;
        }

        ApplySelectedThickness();
        if (_canvas.HasSelection && _canvas.ApplyThicknessToSelection(_canvas.StrokeThickness))
        {
            SetStatus("已更新选中标注的线宽。");
        }
    }

    private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingToolbarFromSelection)
        {
            return;
        }

        ApplySelectedFontSize();
        if (_canvas.HasSelection && _canvas.ApplyFontSizeToSelection(_canvas.TextFontSize))
        {
            SetStatus("已更新选中文字的字号。");
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_canvas.Undo())
        {
            SetStatus("已撤销。");
        }
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_canvas.Redo())
        {
            SetStatus("已重做。");
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var image = BuildExportImage();
            ImageExportService.CopyToClipboard(image);
            MarkExported();
            SetStatus("已复制到剪贴板。");
        }
        catch (Exception ex)
        {
            ShowError("复制失败", ex.Message);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var image = BuildExportImage();
        while (true)
        {
            var path = PromptSavePath();
            if (path is null)
            {
                return;
            }

            try
            {
                ImageExportService.SaveToFile(image, path);
                MarkExported();
                SetStatus($"已保存到 {path}");
                return;
            }
            catch (Exception ex)
            {
                ShowError("保存失败", ex.Message);
            }
        }
    }

    private void CopyAndSaveButton_Click(object sender, RoutedEventArgs e)
    {
        BitmapSource image;
        try
        {
            image = BuildExportImage();
            ImageExportService.CopyToClipboard(image);
        }
        catch (Exception ex)
        {
            ShowError("复制失败", ex.Message);
            return;
        }

        while (true)
        {
            var path = PromptSavePath();
            if (path is null)
            {
                SetStatus("已复制到剪贴板，保存已取消。");
                return;
            }

            try
            {
                ImageExportService.SaveToFile(image, path);
                MarkExported();
                SetStatus($"已复制并保存到 {path}");
                return;
            }
            catch (Exception ex)
            {
                ShowError("保存失败", ex.Message);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            if (_canvas.Undo())
            {
                SetStatus("已撤销。");
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            if (_canvas.Redo())
            {
                SetStatus("已重做。");
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _canvas.DeleteSelection())
        {
            SetStatus("已删除选中标注。");
            e.Handled = true;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _canvas.CommitPendingEdits();

        if (!_hasUnexportedChanges)
        {
            return;
        }

        var result = MessageBox.Show(
            "当前截图还有未导出的内容，确定直接关闭吗？",
            "关闭标注窗口",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void ApplySelectedTool()
    {
        if (!IsLoaded)
        {
            return;
        }

        var selectedButton = FindVisualChildren<RadioButton>(this)
            .FirstOrDefault(button => button.GroupName == "Tools" && button.IsChecked == true);

        if (selectedButton?.Tag is string tag && Enum.TryParse<ToolKind>(tag, out var tool))
        {
            _canvas.CurrentTool = tool;
            SetStatus($"当前工具：{selectedButton.Content}");
        }
    }

    private void ApplySelectedColor()
    {
        if (ColorComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _canvas.StrokeColor = (Color)ColorConverter.ConvertFromString(tag);
        }
    }

    private void ApplySelectedThickness()
    {
        if (ThicknessComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            double.TryParse(tag, out var thickness))
        {
            _canvas.StrokeThickness = thickness;
        }
    }

    private void ApplySelectedFontSize()
    {
        if (FontSizeComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            double.TryParse(tag, out var fontSize))
        {
            _canvas.TextFontSize = fontSize;
        }
    }

    private BitmapSource BuildExportImage()
    {
        _canvas.CommitPendingEdits();
        return ImageExportService.Compose(_canvas.BaseImage, _canvas.Annotations);
    }

    private string? PromptSavePath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存截图",
            Filter = "PNG 图片|*.png",
            DefaultExt = ".png",
            FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (!string.IsNullOrWhiteSpace(_defaultSaveDirectory) && Directory.Exists(_defaultSaveDirectory))
        {
            dialog.InitialDirectory = _defaultSaveDirectory;
        }

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void ApplyInitialSettings(AppSettings settings)
    {
        var colorTag = TryParseHexColor(settings.StrokeColor, out _) ? settings.StrokeColor : "#FFE53E3E";
        SelectOrAddComboBoxItemByTag(ColorComboBox, colorTag, $"自定义 {colorTag}");

        var thicknessTag = settings.StrokeThickness > 0
            ? settings.StrokeThickness.ToString(CultureInfo.InvariantCulture)
            : "4";
        SelectOrAddComboBoxItemByTag(ThicknessComboBox, thicknessTag, $"自定义 {thicknessTag} px");
    }

    private static bool TryParseHexColor(string? value, out Color color)
    {
        color = Colors.Red;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static void SelectOrAddComboBoxItemByTag(System.Windows.Controls.ComboBox comboBox, string tagValue, string customContent)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == tagValue)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        var customItem = new ComboBoxItem
        {
            Content = customContent,
            Tag = tagValue
        };
        comboBox.Items.Insert(0, customItem);
        comboBox.SelectedItem = customItem;
    }

    private void MarkExported()
    {
        _hasUnexportedChanges = false;
        UpdateTitle();
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        UndoButton.IsEnabled = _canvas.CanUndo;
        RedoButton.IsEnabled = _canvas.CanRedo;
        HintTextBlock.Text = _canvas.HasSelection
            ? "已选中标注：可改颜色/线宽/字号；拖动可移动；Delete 删除"
            : "Ctrl+Z 撤销，Ctrl+Y 重做；选择工具下悬停可高亮标注";
    }

    private void SyncToolbarFromSelection()
    {
        if (!_canvas.HasSelection)
        {
            return;
        }

        _syncingToolbarFromSelection = true;
        try
        {
            _canvas.SyncToolbarFromSelection((color, thickness, fontSize) =>
            {
                if (color is Color c)
                {
                    var tag = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                    SelectOrAddComboBoxItemByTag(ColorComboBox, tag, $"自定义 {tag}");
                    _canvas.StrokeColor = c;
                }

                if (thickness is double t)
                {
                    var tag = t.ToString(CultureInfo.InvariantCulture);
                    SelectOrAddComboBoxItemByTag(ThicknessComboBox, tag, $"自定义 {tag} px");
                    _canvas.StrokeThickness = t;
                }

                if (fontSize is double f)
                {
                    var tag = f.ToString(CultureInfo.InvariantCulture);
                    SelectOrAddComboBoxItemByTag(FontSizeComboBox, tag, $"自定义 {tag}");
                    _canvas.TextFontSize = f;
                }
            });
        }
        finally
        {
            _syncingToolbarFromSelection = false;
        }
    }

    private void UpdateTitle()
    {
        Title = _hasUnexportedChanges ? "截图标注 *" : "截图标注";
    }

    private void SetStatus(string message) => StatusTextBlock.Text = message;

    private void ShowError(string title, string message)
    {
        SetStatus(message);
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
