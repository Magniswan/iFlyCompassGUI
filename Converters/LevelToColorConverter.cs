using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace iFlyCompassGUI.Converters;

public class LevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var color = value?.ToString() switch
        {
            "ERROR" => Microsoft.UI.Colors.Red,
            "DEBUG" => Microsoft.UI.Colors.Gray,
            _ => Microsoft.UI.Colors.Green
        };
        return new SolidColorBrush(color);
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
