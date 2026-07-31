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
    private SettingsWindow? _settingsWindow;
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
            ShowHotkeyRegistrationFailure(
                _hotkeyService.LastRegistrationErrorCode,
                DescribeHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey));
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
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var settingsWindow = new SettingsWindow(CloneSettings(_settings))
        {
            Owner = _messageWindow
        };

        _settingsWindow = settingsWindow;
        try
        {
            var dialogResult = settingsWindow.ShowDialog();
            if (dialogResult == true && settingsWindow.ResultSettings is not null)
            {
                ApplySettings(settingsWindow.ResultSettings);
            }
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ExitApplication() => Shutdown();

    private void Overlay_CaptureConfirmed(System.Windows.Media.Imaging.BitmapSource capture)
    {
        var editorWindow = new EditorWindow(capture, _settings);
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

    private void ApplySettings(AppSettings updatedSettings)
    {
        if (_hotkeyService is null)
        {
            return;
        }

        var previousSettings = CloneSettings(_settings);
        var previousHotkeyModifiers = _settings.HotkeyModifiers;
        var previousHotkeyKey = _settings.HotkeyKey;

        _settings.HotkeyModifiers = updatedSettings.HotkeyModifiers;
        _settings.HotkeyKey = updatedSettings.HotkeyKey;
        _settings.DefaultSaveDirectory = updatedSettings.DefaultSaveDirectory;
        _settings.StrokeColor = updatedSettings.StrokeColor;
        _settings.StrokeThickness = updatedSettings.StrokeThickness;

        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            RestoreSettings(previousSettings);
            System.Windows.MessageBox.Show(
                $"保存设置失败：{ex.Message}",
                "截图工具",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _hotkeyService.Unregister();
        if (_hotkeyService.Register())
        {
            return;
        }

        _settings.HotkeyModifiers = previousHotkeyModifiers;
        _settings.HotkeyKey = previousHotkeyKey;

        try
        {
            _settings.Save();
        }
        catch
        {
            // If restoring the old hotkey cannot be saved, keep the in-memory rollback.
        }

        if (!_hotkeyService.Register())
        {
            ShowHotkeyRegistrationFailure(
                _hotkeyService.LastRegistrationErrorCode,
                DescribeHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey));
        }
    }

    private static AppSettings CloneSettings(AppSettings settings) =>
        new()
        {
            HotkeyModifiers = settings.HotkeyModifiers,
            HotkeyKey = settings.HotkeyKey,
            DefaultSaveDirectory = settings.DefaultSaveDirectory,
            StrokeColor = settings.StrokeColor,
            StrokeThickness = settings.StrokeThickness
        };

    private void RestoreSettings(AppSettings settings)
    {
        _settings.HotkeyModifiers = settings.HotkeyModifiers;
        _settings.HotkeyKey = settings.HotkeyKey;
        _settings.DefaultSaveDirectory = settings.DefaultSaveDirectory;
        _settings.StrokeColor = settings.StrokeColor;
        _settings.StrokeThickness = settings.StrokeThickness;
    }

    private static string DescribeHotkey(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & 0x0001) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & 0x0004) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & 0x0008) != 0)
        {
            parts.Add("Win");
        }

        var keyName = key == 0 ? "未设置" : ((System.Windows.Input.Key)System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)key)).ToString();
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private void ShowHotkeyRegistrationFailure(int errorCode, string hotkeyDescription)
    {
        var detail = errorCode == 0
            ? "请检查是否被其他程序占用。"
            : $"错误代码：{errorCode}。请检查是否被其他程序占用。";
        System.Windows.MessageBox.Show(
            $"热键 {hotkeyDescription} 注册失败，应用将继续运行，但无法通过热键启动截图。\n{detail}",
            "截图工具",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
