using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace iFlyCompassGUI.Converters;

public class LevelToTerminalColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var brush = value?.ToString() switch
        {
            "ERROR" => new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            "DEBUG" => new SolidColorBrush(Microsoft.UI.Colors.DimGray),
            _ => new SolidColorBrush(Microsoft.UI.Colors.LightGray)
        };
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
