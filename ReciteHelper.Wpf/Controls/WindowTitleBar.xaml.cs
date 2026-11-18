using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace ReciteHelper.Wpf.Controls;

public partial class WindowTitleBar : UserControl
{
    private static readonly DependencyPropertyKey FrameInsetsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FrameInsets), typeof(Thickness), typeof(WindowTitleBar), new PropertyMetadata(default(Thickness)));
    public static readonly DependencyProperty FrameInsetsProperty = FrameInsetsPropertyKey.DependencyProperty;
    public Thickness FrameInsets => (Thickness)GetValue(FrameInsetsProperty);

    private Window? _window;
    private WindowChrome? _maximizedChrome;

    public WindowTitleBar()
    {
        InitializeComponent();
        Loaded += BindFrame;
        Unloaded += UnbindFrame;
    }

    private void BindFrame(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        _window.StateChanged += UpdateFrame;
        _window.SizeChanged += UpdateFrame;
        _window.LocationChanged += UpdateFrame;
        _window.DpiChanged += UpdateFrame;
        UpdateFrame(_window, EventArgs.Empty);
    }

    private void UnbindFrame(object sender, RoutedEventArgs e)
    {
        if (_window is not { } window) return;
        window.StateChanged -= UpdateFrame;
        window.SizeChanged -= UpdateFrame;
        window.LocationChanged -= UpdateFrame;
        window.DpiChanged -= UpdateFrame;
        _window = null;
    }

    private void UpdateFrame(object? sender, EventArgs e)
    {
        if (_window is not { } window) return;
        if (window.WindowState != WindowState.Maximized)
        {
            if (_maximizedChrome is null) return;
            _maximizedChrome = null;
            SetValue(FrameInsetsPropertyKey, default(Thickness));
            window.ClearValue(WindowChrome.WindowChromeProperty);
            return;
        }

        // The native maximized rectangle includes invisible resize margins outside the monitor work area.
        const uint nearestMonitor = 2;
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, nearestMonitor);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(handle, out var bounds) || !GetMonitorInfoW(monitor, ref info)) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var work = info.WorkArea;
        var insets = new Thickness(
            Math.Max(0, work.Left - bounds.Left) / dpi.DpiScaleX,
            Math.Max(0, work.Top - bounds.Top) / dpi.DpiScaleY,
            Math.Max(0, bounds.Right - work.Right) / dpi.DpiScaleX,
            Math.Max(0, bounds.Bottom - work.Bottom) / dpi.DpiScaleY);
        SetValue(FrameInsetsPropertyKey, insets);

        var captionHeight = ActualHeight + insets.Top;
        if (_maximizedChrome is null)
        {
            _maximizedChrome = new WindowChrome
            {
                CaptionHeight = captionHeight,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, _maximizedChrome);
        }
        else
        {
            _maximizedChrome.CaptionHeight = captionHeight;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => Window.GetWindow(this).WindowState = WindowState.Minimized;

    private void OpenWindowMenu_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        ResizeItem.Visibility = window.ResizeMode == ResizeMode.CanMinimize ? Visibility.Collapsed : Visibility.Visible;
        ResizeItem.Header = window.WindowState == WindowState.Maximized ? "还原" : "最大化";
        WindowMenu.PlacementTarget = WindowMenuButton;
        WindowMenu.IsOpen = true;
    }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(Window.GetWindow(this));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
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
}
