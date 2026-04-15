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

        var localizer = LocalizationManager.Instance;
        return status switch
        {
            ManagedTaskStatus.Running => localizer.GetString("TaskCenterPage.StatusRunning", "运行中"),
            ManagedTaskStatus.Completed => localizer.GetString("TaskCenterPage.StatusCompleted", "已完成"),
            ManagedTaskStatus.Cancelled => localizer.GetString("TaskCenterPage.StatusCancelled", "已取消"),
            ManagedTaskStatus.Failed => localizer.GetString("TaskCenterPage.StatusFailed", "失败"),
            _ => status.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
