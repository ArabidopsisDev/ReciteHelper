using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ReciteHelper.Wpf.Utilities;

internal static class NativeWindowBounds
{
    internal static void PreferSmallCorners(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        const int cornerPreference = 33;
        const int roundSmall = 3;
        var handle = new WindowInteropHelper(window).Handle;
        DwmSetWindowAttribute(handle, cornerPreference, roundSmall, sizeof(int));
    }

    internal static bool TryGet(Window window, out Rect bounds, out Rect workArea)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (GetWindowRect(handle, out var nativeBounds) && GetMonitorInfoW(monitor, ref info))
        {
            bounds = nativeBounds.ToRect();
            workArea = info.WorkArea.ToRect();
            return true;
        }

        bounds = workArea = Rect.Empty;
        return false;
    }

    internal static void Place(Window window, Rect bounds, bool resize)
    {
        const uint noSize = 0x0001;
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
            (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height),
            noZOrder | noActivate | (resize ? 0 : noSize));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Bounds, WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, in int value, int size);
}
