using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ReciteHelper.Utils.Converter;

public class BooleanToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isCorrect)
        {
            return isCorrect ?
                new SolidColorBrush(Color.FromRgb(40, 167, 69)) :
                new SolidColorBrush(Color.FromRgb(220, 53, 69));
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
