using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ReciteHelper.Utils.Converter;

public class FileExtensionToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string extension)
        {
            return extension.ToUpper() switch
            {
                "DOCX" => new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                "PPTX" => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                "PDF" => new SolidColorBrush(Color.FromRgb(220, 53, 69)),
                "TXT" => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
                "MEG" => new SolidColorBrush(Color.FromRgb(145, 132, 238)),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}