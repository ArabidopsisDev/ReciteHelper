using System.Globalization;
using System.Windows.Data;

namespace ReciteHelper.Utils.Converter;

public class FileExtensionToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string extension)
        {
            return extension.ToUpper() switch
            {
                "DOCX" => "W",
                "PPTX" => "P",
                "PDF" => "D",
                "TXT" => "T",
                "MEG" => "M",
                _ => "?"
            };
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}