using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace iFlyCompassGUI.Converters;

public class StepToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int step && parameter is string s && int.TryParse(s, out var target))
        {
            return new SolidColorBrush(step >= target ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Gray);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
