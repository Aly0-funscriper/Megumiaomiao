using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VideoShelf.Controls;

public sealed class MpvPlayerHost : HwndHost
{
    private IntPtr _handle;
    private bool _suppressNextButtonUp;
    public new IntPtr Handle => _handle;
    public event EventHandler? HandleCreated;
    public event EventHandler? DoubleClicked;
    public event EventHandler? Clicked;
    public event EventHandler<bool>? MouseBottomChanged;
    public event EventHandler<int>? KeyPressed;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _handle = CreateWindowEx(0, "static", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | SS_NOTIFY,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight),
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("无法创建 mpv 播放宿主窗口。");
        HandleCreated?.Invoke(this, EventArgs.Empty);
        return new HandleRef(this, _handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) => DestroyWindow(hwnd.Handle);

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0203) // WM_LBUTTONDBLCLK
        {
            _suppressNextButtonUp = true;
            DoubleClicked?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (msg == 0x0201) // WM_LBUTTONDOWN
        {
            SetFocus(hwnd);
        }
        else if (msg == 0x0202) // WM_LBUTTONUP
        {
            if (_suppressNextButtonUp) _suppressNextButtonUp = false;
            else Clicked?.Invoke(this, EventArgs.Empty);
        }
        else if (msg == 0x0200) // WM_MOUSEMOVE
        {
            int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xffff));
            MouseBottomChanged?.Invoke(this, y >= Math.Max(0, ActualHeight - 100));
        }
        else if (msg == 0x0100) // WM_KEYDOWN
        {
            KeyPressed?.Invoke(this, wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int SS_NOTIFY = 0x00000100;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
        int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);
}
