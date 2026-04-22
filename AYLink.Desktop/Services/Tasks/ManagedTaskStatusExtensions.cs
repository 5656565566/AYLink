using AYLink.Desktop.Services.Localization;

namespace AYLink.Desktop.Services.Tasks;

public static class ManagedTaskStatusExtensions
{
    public static string ToLocalizedString(this ManagedTaskStatus status)
    {
        var localizer = LocalizationManager.Instance;
        return status switch
        {
            ManagedTaskStatus.Running => localizer.GetString("TaskPage.StatusRunning", "运行中"),
            ManagedTaskStatus.Completed => localizer.GetString("TaskPage.StatusCompleted", "已完成"),
            ManagedTaskStatus.Cancelled => localizer.GetString("TaskPage.StatusCancelled", "已取消"),
            ManagedTaskStatus.Failed => localizer.GetString("TaskPage.StatusFailed", "失败"),
            _ => status.ToString()
        };
    }
}
