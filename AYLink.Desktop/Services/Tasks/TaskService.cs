using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Services.Tasks;

public interface ITaskContext
{
    void UpdateProgress(double value, string? newMessage = null);

    void Close(string? completedMessage = null);

    void Fail(string? failedMessage = null);

    void Cancel(string? cancelledMessage = null);
}

public enum ManagedTaskStatus
{
    Running,
    Completed,
    Cancelled,
    Failed
}

public partial class ManagedTaskItem : ObservableObject
{
    [ObservableProperty]
    public partial string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ManagedTaskStatus Status { get; set; } = ManagedTaskStatus.Running;

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool ShowProgress { get; set; } = true;

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCancelable { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    public partial DateTimeOffset? StartedAt { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    public partial DateTimeOffset? FinishedAt { get; set; }
    public Action? CancelAction { get; set; }

    public bool CanCancel => IsCancelable && Status == ManagedTaskStatus.Running && CancelAction != null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CancelAction?.Invoke();
    }

    partial void OnStatusChanged(ManagedTaskStatus value)
    {
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCancelableChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
    }
}

public class ManagedTaskOptions
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public bool IsCancelable { get; init; }

    public bool IsIndeterminate { get; init; } = true;

    public bool ShowProgress { get; init; } = true;

    public Action? CancelAction { get; init; }
}

public sealed class TaskService
{
    private sealed class TaskContext : ITaskContext
    {
        private readonly ManagedTaskItem _task;

        public TaskContext(ManagedTaskItem task)
        {
            _task = task;
        }

        public void UpdateProgress(double value, string? newMessage = null)
        {
            TaskService.Instance.UpdateTask(_task, progress: value, detail: newMessage, isIndeterminate: false, showProgress: true);
        }

        public void Close(string? completedMessage = null)
        {
            TaskService.Instance.CompleteTask(_task, completedMessage);
        }

        public void Fail(string? failedMessage = null)
        {
            TaskService.Instance.FailTask(_task, failedMessage);
        }

        public void Cancel(string? cancelledMessage = null)
        {
            TaskService.Instance.CancelTask(_task, cancelledMessage);
        }
    }

    public static TaskService Instance { get; } = new();

    private readonly NotificationService _notifications = NotificationService.Instance;

    public ObservableCollection<ManagedTaskItem> Tasks { get; } = [];

    public ObservableCollection<ManagedTaskItem> RunningTasks { get; } = [];

    public ObservableCollection<ManagedTaskItem> CompletedTasks { get; } = [];

    public ObservableCollection<ManagedTaskItem> CancelledTasks { get; } = [];

    public ObservableCollection<ManagedTaskItem> FailedTasks { get; } = [];

    private TaskService()
    {
    }

    public ManagedTaskItem StartTask(ManagedTaskOptions options)
    {
        var task = new ManagedTaskItem
        {
            Title = options.Title,
            Description = options.Description,
            Detail = options.Description,
            Source = options.Source,
            IsCancelable = options.IsCancelable,
            IsIndeterminate = options.IsIndeterminate,
            ShowProgress = options.ShowProgress,
            CancelAction = options.CancelAction,
            Status = ManagedTaskStatus.Running,
            Severity = InfoBarSeverity.Informational,
            Progress = 0,
            CreatedAt = DateTimeOffset.Now,
            StartedAt = DateTimeOffset.Now,
            FinishedAt = null
        };

        Dispatcher.UIThread.Post(() =>
        {
            Tasks.Insert(0, task);
            RebuildGroups();
        });

        return task;
    }

    public ITaskContext ShowProgress(
        string title,
        string message,
        string? source = null,
        Action? cancelAction = null,
        bool isIndeterminate = true,
        bool showProgress = true)
    {
        var task = StartTask(new ManagedTaskOptions
        {
            Title = title,
            Description = message,
            Source = source ?? "Task",
            IsCancelable = cancelAction != null,
            CancelAction = cancelAction,
            IsIndeterminate = isIndeterminate,
            ShowProgress = showProgress
        });

        _notifications.ShowInfo(title, message);
        return new TaskContext(task);
    }

    public void UpdateTask(
        ManagedTaskItem? task,
        double? progress = null,
        string? detail = null,
        bool? isIndeterminate = null,
        bool? showProgress = null)
    {
        if (task == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (progress.HasValue)
            {
                task.Progress = Math.Clamp(progress.Value, 0, 100);
            }

            if (detail != null)
            {
                task.Detail = detail;
            }

            if (isIndeterminate.HasValue)
            {
                task.IsIndeterminate = isIndeterminate.Value;
            }

            if (showProgress.HasValue)
            {
                task.ShowProgress = showProgress.Value;
            }
        });
    }

    public void CompleteTask(ManagedTaskItem? task, string? detail = null)
    {
        FinishTask(task, ManagedTaskStatus.Completed, InfoBarSeverity.Success, detail);
    }

    public void CancelTask(ManagedTaskItem? task, string? detail = null)
    {
        FinishTask(task, ManagedTaskStatus.Cancelled, InfoBarSeverity.Warning, detail);
    }

    public void FailTask(ManagedTaskItem? task, string? detail = null)
    {
        FinishTask(task, ManagedTaskStatus.Failed, InfoBarSeverity.Error, detail);
    }

    public void RemoveTask(ManagedTaskItem? task)
    {
        if (task == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Tasks.Remove(task);
            RebuildGroups();
        });
    }

    public void ClearHistory()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var running = Tasks.Where(x => x.Status == ManagedTaskStatus.Running).ToList();
            Tasks.Clear();

            foreach (var item in running)
            {
                Tasks.Add(item);
            }

            RebuildGroups();
        });
    }

    private void FinishTask(ManagedTaskItem? task, ManagedTaskStatus status, InfoBarSeverity severity, string? detail)
    {
        if (task == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            task.Status = status;
            task.Severity = severity;
            task.IsCancelable = false;
            task.CancelAction = null;
            task.IsIndeterminate = false;
            task.Progress = status == ManagedTaskStatus.Completed ? 100 : task.Progress;
            task.FinishedAt = DateTimeOffset.Now;

            if (detail != null)
            {
                task.Detail = detail;
            }

            RebuildGroups();
        });
    }

    private void RebuildGroups()
    {
        RebuildGroup(RunningTasks, ManagedTaskStatus.Running);
        RebuildGroup(CompletedTasks, ManagedTaskStatus.Completed);
        RebuildGroup(CancelledTasks, ManagedTaskStatus.Cancelled);
        RebuildGroup(FailedTasks, ManagedTaskStatus.Failed);
    }

    private void RebuildGroup(ObservableCollection<ManagedTaskItem> target, ManagedTaskStatus status)
    {
        var items = Tasks.Where(x => x.Status == status).OrderByDescending(x => x.CreatedAt).ToList();
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
