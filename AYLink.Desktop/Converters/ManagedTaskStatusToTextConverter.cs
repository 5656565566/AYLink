using Avalonia.Data.Converters;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Localization;
using System;
using System.Globalization;

namespace AYLink.Desktop.Converters;

public class ManagedTaskStatusToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ManagedTaskStatus status)
        {
            return string.Empty;
        }

        return status.ToLocalizedString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
