using ReciteHelper.Wpf.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace ReciteHelper.Wpf.Controls;

public partial class WindowTitleBar : UserControl
{
    private static readonly DependencyPropertyKey FrameInsetsPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "FrameInsets", typeof(Thickness), typeof(WindowTitleBar), new PropertyMetadata(default(Thickness)));
    public static readonly DependencyProperty FrameInsetsProperty = FrameInsetsPropertyKey.DependencyProperty;
    public static Thickness GetFrameInsets(DependencyObject target) => (Thickness)target.GetValue(FrameInsetsProperty);

    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle), typeof(bool), typeof(WindowTitleBar), new PropertyMetadata(true));

    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

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
            window.SetValue(FrameInsetsPropertyKey, default(Thickness));
            window.ClearValue(WindowChrome.WindowChromeProperty);
            return;
        }

        // The native maximized rectangle includes invisible resize margins outside the monitor work area.
        if (!NativeWindowBounds.TryGet(window, out var bounds, out var work)) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var insets = new Thickness(
            Math.Max(0, work.Left - bounds.Left) / dpi.DpiScaleX,
            Math.Max(0, work.Top - bounds.Top) / dpi.DpiScaleY,
            Math.Max(0, bounds.Right - work.Right) / dpi.DpiScaleX,
            Math.Max(0, bounds.Bottom - work.Bottom) / dpi.DpiScaleY);
        window.SetValue(FrameInsetsPropertyKey, insets);

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

}
