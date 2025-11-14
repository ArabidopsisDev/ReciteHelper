using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ReciteHelper.Utils.Converter;

public class MasteryLevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double masteryLevel)
        {
            if (masteryLevel >= 80)
                return new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green
            else if (masteryLevel >= 60)
                return new SolidColorBrush(Color.FromRgb(255, 193, 7));  // Yellow
            else if (masteryLevel >= 40)
                return new SolidColorBrush(Color.FromRgb(253, 126, 20)); // Orange
            else
                return new SolidColorBrush(Color.FromRgb(220, 53, 69));  // Red
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
