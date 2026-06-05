using Microsoft.UI.Xaml.Data;

namespace iFlyCompassGUI.Converters;

public class BoolToStartStopConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? "停止服务" : "启动服务";
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
