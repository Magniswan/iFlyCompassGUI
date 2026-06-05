using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace iFlyCompassGUI.Converters;

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int i && parameter is string s && int.TryParse(s, out var target))
            return i == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
