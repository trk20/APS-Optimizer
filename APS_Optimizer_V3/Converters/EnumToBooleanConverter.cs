using Microsoft.UI.Xaml.Data;

namespace APS_Optimizer_V3.Converters;
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter == null || value == null)
            return DependencyProperty.UnsetValue;

        string? parameterString = parameter.ToString();
        if (string.IsNullOrWhiteSpace(parameterString))
            return DependencyProperty.UnsetValue;

        // Check if the value matches parameter
        if (Enum.TryParse(value.GetType(), parameterString, true, out object? parameterValue))
        {
            return value.Equals(parameterValue);
        }

        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (parameter == null || value == null || !(value is bool))
            return DependencyProperty.UnsetValue;

        string? parameterString = parameter.ToString();
        if (string.IsNullOrWhiteSpace(parameterString))
            return DependencyProperty.UnsetValue;

        // If the radio button is checked (true), return the enum value it represents
        // Otherwise return default or UnsetValue (doesn't uncheck others automatically here)
        return (bool)value ? Enum.Parse(targetType, parameterString, true) : DependencyProperty.UnsetValue;
    }
}