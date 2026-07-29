using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ScreenshotTool.Native;

namespace ScreenshotTool.Shell;

public sealed class HotkeyService : IDisposable
{
    internal const int RegisteredHotkeyId = 0x5001;

    private readonly Window _messageWindow;
    private readonly AppSettings _settings;
    private HwndSource? _hwndSource;
    private bool _hookAttached;
    private bool _isRegistered;
    private bool _disposed;

    public HotkeyService(Window messageWindow, AppSettings settings)
    {
        _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public event Action? HotkeyPressed;

    public int LastRegistrationErrorCode { get; private set; }

    public bool Register()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isRegistered)
        {
            return true;
        }

        EnsureHook();

        var handle = new WindowInteropHelper(_messageWindow).EnsureHandle();
        var registered = Win32.RegisterHotKey(handle, RegisteredHotkeyId, _settings.HotkeyModifiers, _settings.HotkeyKey);
        LastRegistrationErrorCode = registered ? 0 : Marshal.GetLastWin32Error();
        _isRegistered = registered;
        return registered;
    }

    public void Unregister()
    {
        if (_disposed || !_isRegistered)
        {
            return;
        }

        var handle = new WindowInteropHelper(_messageWindow).EnsureHandle();
        Win32.UnregisterHotKey(handle, RegisteredHotkeyId);
        _isRegistered = false;
        LastRegistrationErrorCode = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();

        if (_hookAttached && _hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
        }

        _hookAttached = false;
        _hwndSource = null;
        _disposed = true;
    }

    internal static bool MatchesRegisteredHotkeyMessage(int message, IntPtr hotkeyIdentifier) =>
        message == Win32.WM_HOTKEY && hotkeyIdentifier == (IntPtr)RegisteredHotkeyId;

    private void EnsureHook()
    {
        if (_hookAttached)
        {
            return;
        }

        var handle = new WindowInteropHelper(_messageWindow).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("Unable to initialize hotkey message source.");
        _hwndSource.AddHook(WndProc);
        _hookAttached = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (MatchesRegisteredHotkeyMessage(message, wParam))
        {
            handled = true;
            HotkeyPressed?.Invoke();
        }

        return IntPtr.Zero;
    }
}
