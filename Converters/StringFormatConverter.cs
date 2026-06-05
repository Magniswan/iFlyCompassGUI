using Microsoft.UI.Xaml.Data;

namespace iFlyCompassGUI.Converters;

public class StringFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null) return string.Empty;
        var format = parameter as string ?? "{0}";
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
