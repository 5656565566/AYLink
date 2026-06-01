using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;
using System;
using System.Globalization;

namespace AYLink.Desktop.Converters;

public class ConnectionTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string connectionType)
        {
            if (connectionType.Equals("WiFi", StringComparison.OrdinalIgnoreCase) ||
                connectionType.Equals("Wi-Fi", StringComparison.OrdinalIgnoreCase))
            {
                return Symbol.Wifi4;
            }
            else if (connectionType.Equals("USB", StringComparison.OrdinalIgnoreCase))
            {
                return Symbol.Link;
            }
        }
        return Symbol.Link; // Default
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
