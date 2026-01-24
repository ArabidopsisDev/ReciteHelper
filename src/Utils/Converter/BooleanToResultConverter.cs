using System.Globalization;
using System.Windows.Data;

namespace ReciteHelper.Utils.Converter;

public class BooleanToResultConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isCorrect)
        {
            return isCorrect ? "正确" : "错误";
        }
        return "未知";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}