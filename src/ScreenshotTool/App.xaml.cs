using System.Threading;
using System.Windows;

namespace ScreenshotTool;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Global\\ScreenshotTool.SingleInstance";
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
