using System.Threading;
using System.Windows;
using ScreenshotTool.Capture;
using ScreenshotTool.Editor;
using ScreenshotTool.Shell;

namespace ScreenshotTool;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Global\\ScreenshotTool.SingleInstance";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private AppSettings _settings = new();
    private TrayIconService? _trayIconService;
    private HotkeyService? _hotkeyService;
    private Window? _messageWindow;
    private CaptureOverlayWindow? _overlayWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("截图工具已在托盘运行。", "截图工具",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = AppSettings.Load();
        _messageWindow = CreateMessageWindow();
        _hotkeyService = new HotkeyService(_messageWindow, _settings);
        _hotkeyService.HotkeyPressed += StartCapture;

        _trayIconService = new TrayIconService(StartCapture, OpenSettings, ExitApplication);

        if (!_hotkeyService.Register())
        {
            ShowHotkeyRegistrationFailure(_hotkeyService.LastRegistrationErrorCode);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hotkeyService is not null)
        {
            _hotkeyService.HotkeyPressed -= StartCapture;
            _hotkeyService.Dispose();
        }

        _trayIconService?.Dispose();

        if (_messageWindow is not null)
        {
            _messageWindow.Close();
            _messageWindow = null;
        }

        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void StartCapture()
    {
        if (_overlayWindow is not null)
        {
            return;
        }

        try
        {
            var overlay = CaptureOverlayWindow.ShowNew();
            _overlayWindow = overlay;
            overlay.CaptureConfirmed += Overlay_CaptureConfirmed;
            overlay.CaptureCancelled += Overlay_CaptureCancelled;
            overlay.Closed += Overlay_Closed;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"启动截图失败：{ex.Message}", "截图工具",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSettings()
    {
        System.Windows.MessageBox.Show("设置将在下一任务提供。", "截图工具",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitApplication() => Shutdown();

    private void Overlay_CaptureConfirmed(System.Windows.Media.Imaging.BitmapSource capture)
    {
        var editorWindow = new EditorWindow(capture);
        editorWindow.Show();
        editorWindow.Activate();
    }

    private static void Overlay_CaptureCancelled()
    {
    }

    private void Overlay_Closed(object? sender, EventArgs e)
    {
        if (sender is not CaptureOverlayWindow overlayWindow)
        {
            return;
        }

        overlayWindow.CaptureConfirmed -= Overlay_CaptureConfirmed;
        overlayWindow.CaptureCancelled -= Overlay_CaptureCancelled;
        overlayWindow.Closed -= Overlay_Closed;

        if (ReferenceEquals(_overlayWindow, overlayWindow))
        {
            _overlayWindow = null;
        }
    }

    private static Window CreateMessageWindow() =>
        new()
        {
            Width = 0,
            Height = 0,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0,
            Left = -10000,
            Top = -10000
        };

    private void ShowHotkeyRegistrationFailure(int errorCode)
    {
        var detail = errorCode == 0
            ? "请检查是否被其他程序占用。"
            : $"错误代码：{errorCode}。请检查是否被其他程序占用。";
        System.Windows.MessageBox.Show(
            $"默认热键 Ctrl+Shift+A 注册失败，应用将继续运行，但无法通过热键启动截图。\n{detail}",
            "截图工具",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
