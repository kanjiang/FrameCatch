using System.Threading;
using System.Windows;
using ScreenshotTool.Capture;
using ScreenshotTool.Editor;

namespace ScreenshotTool;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Global\\ScreenshotTool.SingleInstance";
    private Mutex? _mutex;
    private bool _ownsMutex;

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
        // 后续 Task 接入 TrayIconService；临时：先不 Show 任何窗口

#if DEBUG
        // Task 10 会用托盘/热键替换这个调试入口；当前可按 Esc 或右键取消。
        var overlay = CaptureOverlayWindow.ShowNew();
        overlay.CaptureConfirmed += capture =>
        {
            var editor = new EditorWindow(capture);
            editor.Closed += (_, _) =>
            {
                if (Current.Windows.OfType<Window>().All(window => !window.IsVisible))
                {
                    Dispatcher.BeginInvoke(new Action(Shutdown));
                }
            };

            editor.Show();
        };
        overlay.CaptureCancelled += () =>
        {
            if (Current.Windows.OfType<EditorWindow>().All(window => !window.IsVisible))
            {
                Dispatcher.BeginInvoke(new Action(Shutdown));
            }
        };
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
