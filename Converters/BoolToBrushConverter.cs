using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace iFlyCompassGUI.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Green);
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Red);
    
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? TrueBrush : FalseBrush;
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
