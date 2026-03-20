using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Cominomi.WinUI;

internal static class WindowHelper
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void BringToForeground(Microsoft.UI.Xaml.Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero) return;

        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        window.Activate();
    }
}
