using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ScreenshotTool.Shell;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private bool _disposed;

    public TrayIconService(Action startCapture, Action openSettings, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(startCapture);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(exitApplication);

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("截图", null, (_, _) => startCapture());
        _contextMenu.Items.Add("设置", null, (_, _) => openSettings());
        _contextMenu.Items.Add("退出", null, (_, _) => exitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "截图工具",
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

        void NotifyIcon_DoubleClick(object? sender, EventArgs e) => startCapture();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var extractedIcon = Icon.ExtractAssociatedIcon(processPath);
            if (extractedIcon is not null)
            {
                return extractedIcon;
            }
        }

        return SystemIcons.Application;
    }
}
