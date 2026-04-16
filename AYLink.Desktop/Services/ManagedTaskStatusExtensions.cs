using AYLink.Desktop.Services.Localization;

namespace AYLink.Desktop.Services;

public static class ManagedTaskStatusExtensions
{
    public static string ToLocalizedString(this ManagedTaskStatus status)
    {
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
}
