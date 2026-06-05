using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Text;

namespace iFlyCompassGUI.Converters;

public class StepToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int step && parameter is string s && int.TryParse(s, out var target))
        {
            return step == target ? FontWeights.SemiBold : FontWeights.Normal;
        }
        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
