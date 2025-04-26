using Microsoft.UI.Xaml.Data;

namespace APS_Optimizer_V3.Converters; // Adjust namespace

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return !(value is bool b && b);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return !(value is bool b && b);
    }
}