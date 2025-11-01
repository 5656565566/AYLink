using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;
using AYLink.UI.Themes;

namespace AYLink.Utils.BindingConverter;

public class StringToNullableNumber : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return string.Empty;
        }
        return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string input || string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (double.TryParse(input, NumberStyles.Any, culture, out double result))
        {
            return result;
        }

        return new BindingNotification(new InvalidCastException(L.Tr("BindingNotification_Error")), BindingErrorType.Error);
    }
}