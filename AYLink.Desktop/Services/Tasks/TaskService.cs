using System;
using System.Collections.ObjectModel;
using AYLink.Desktop.Services.Notifications;

namespace AYLink.Desktop.Services.Tasks;

public interface ITaskContext
{
    void UpdateProgress(double value, string? newMessage = null);

    void Close(string? completedMessage = null);

    void Fail(string? failedMessage = null);

    void Cancel(string? cancelledMessage = null);
}

public sealed class TaskService
{
    private readonly IUiDispatcher _uiDispatcher = UiDispatcher.Instance;

    private sealed class TaskContext(TaskItem task) : ITaskContext
    {
        private readonly TaskItem _task = task;

        public void UpdateProgress(double value, string? newMessage = null)
        {
            Instance.Update(_task, progress: value, detail: newMessage);
        }

        public void Close(string? completedMessage = null)
        {
            Instance.Complete(_task, completedMessage);
        }

        public void Fail(string? failedMessage = null)
        {
            Instance.Fail(_task, failedMessage);
        }

        public void Cancel(string? cancelledMessage = null)
        {
            Instance.Cancel(_task, cancelledMessage);
        }
    }

    public static TaskService Instance { get; } = new();

    private readonly INotificationService _notifications = NotificationService.Instance;

    public ObservableCollection<TaskItem> Items { get; } = [];

    private TaskService()
    {
    }

    public TaskItem Start(TaskStartOptions options)
    {
        var item = new TaskItem
        {
            Title = options.Title,
            Description = options.Description,
            Detail = options.Description,
            Source = options.Source,
            IsCancelable = options.IsCancelable,
            IsIndeterminate = options.IsIndeterminate,
            CancelAction = options.CancelAction,
            Status = TaskItemStatus.Running,
            CreatedAt = DateTimeOffset.Now
        };

        _uiDispatcher.Post(() => Items.Insert(0, item));
        _notifications.ShowInfo("任务开始", item.Title);
        return item;
    }

    public ITaskContext ShowProgress(
        string title,
        string message,
        string? source = null,
        Action? cancelAction = null,
        bool isIndeterminate = true
        )
    {
        var task = Start(new TaskStartOptions
        {
            Title = title,
            Description = message,
            Source = source ?? "Task",
            IsCancelable = cancelAction != null,
            CancelAction = cancelAction,
            IsIndeterminate = isIndeterminate
        });

        _notifications.ShowInfo(title, message);
        return new TaskContext(task);
    }

    public void Update(TaskItem? item, double? progress = null, string? detail = null)
    {
        if (item == null)
        {
            return;
        }

        _uiDispatcher.Post(() =>
        {
            if (progress.HasValue)
            {
                item.Progress = Math.Clamp(progress.Value, 0, 100);
            }

            if (detail != null)
            {
                item.Detail = detail;
            }
        });
    }

    public void Complete(TaskItem? item, string? detail = null)
    {
        Finish(item, TaskItemStatus.Completed, detail, isError: false);
    }

    public void Fail(TaskItem? item, string? detail = null)
    {
        Finish(item, TaskItemStatus.Failed, detail, isError: true);
    }

    public void Cancel(TaskItem? item, string? detail = null)
    {
        Finish(item, TaskItemStatus.Cancelled, detail, isError: false);
    }

    public void RequestRemove(TaskItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.Status == TaskItemStatus.Running)
        {
            if (!item.CanCancel)
            {
                return;
            }

            _uiDispatcher.Post(() => item.RemoveWhenStopped = true);
            item.CancelCommand.Execute(null);
            return;
        }

        Remove(item);
    }

    public void Remove(TaskItem? item)
    {
        if (item == null || item.Status == TaskItemStatus.Running)
        {
            return;
        }

        _uiDispatcher.Post(() => Items.Remove(item));
    }

    private void Finish(TaskItem? item, TaskItemStatus status, string? detail, bool isError)
    {
        if (item == null)
        {
            return;
        }

        _uiDispatcher.Post(() =>
        {
            item.Status = status;
            item.IsCancelable = false;
            item.IsIndeterminate = false;
            item.Progress = status == TaskItemStatus.Completed ? 100 : item.Progress;
            item.FinishedAt = DateTimeOffset.Now;

            if (detail != null)
            {
                item.Detail = detail;
            }

            if (item.RemoveWhenStopped)
            {
                item.RemoveWhenStopped = false;
                Items.Remove(item);
            }
        });

        if (isError)
        {
            _notifications.ShowError("任务失败", item.Title);
        }
        else if (status == TaskItemStatus.Completed)
        {
            _notifications.ShowSuccess("任务完成", item.Title);
        }
        else if (status == TaskItemStatus.Cancelled)
        {
            _notifications.ShowWarning("任务取消", item.Title);
        }
    }
}
