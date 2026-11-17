using System.Windows.Media;

namespace ReciteHelper.Wpf.Utilities;

public static class AppFonts
{
    public static FontFamily Sans { get; } = new(new Uri("pack://application:,,,/"), "/ReciteHelper.Wpf;component/Fonts/#Noto Sans CJK SC");
    public static FontFamily Serif { get; } = new(new Uri("pack://application:,,,/"), "/ReciteHelper.Wpf;component/Fonts/#Noto Serif CJK SC");
}
