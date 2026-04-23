using AYLink.Desktop.Services.Localization;

namespace AYLink.Desktop.Services.Tasks;

public static class TaskItemStatusExtensions
{
    public static string ToLocalizedString(this TaskItemStatus status)
    {
        var localizer = LocalizationManager.Instance;
        return status switch
        {
            TaskItemStatus.Running => localizer.GetString("TaskPage.StatusRunning", "运行中"),
            TaskItemStatus.Completed => localizer.GetString("TaskPage.StatusCompleted", "已完成"),
            TaskItemStatus.Cancelled => localizer.GetString("TaskPage.StatusCancelled", "已取消"),
            TaskItemStatus.Failed => localizer.GetString("TaskPage.StatusFailed", "失败"),
            _ => status.ToString()
        };
    }
}
