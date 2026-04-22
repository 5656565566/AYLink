using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Services;

/// <summary>
/// 任务中心状态枚举 - 对应任务管理页的四个分组
/// </summary>
public enum ManagedTaskStatus
{
    Running,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// 可管理任务条目模型 - 统一描述一个可取消的耗时任务
/// </summary>
public partial class ManagedTaskItem : ObservableObject
{
    /// <summary>
    /// 任务唯一标识
    /// </summary>
    [ObservableProperty]
    public partial string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 任务标题
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>
    /// 任务简介
    /// </summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>
    /// 任务详情/最新进度描述
    /// </summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>
    /// 任务来源模块（当前也用作筛选标签）
    /// </summary>
    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    /// <summary>
    /// 当前任务状态
    /// </summary>
    [ObservableProperty]
    public partial ManagedTaskStatus Status { get; set; } = ManagedTaskStatus.Running;

    /// <summary>
    /// 与状态对应的提示级别 供页面/Toast 复用
    /// </summary>
    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    /// <summary>
    /// 当前进度值（0-100）
    /// </summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>
    /// 是否显示进度条
    /// </summary>
    [ObservableProperty]
    public partial bool ShowProgress { get; set; } = true;

    /// <summary>
    /// 当前是否为不定进度
    /// </summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; } = true;

    /// <summary>
    /// 当前任务是否允许取消
    /// </summary>
    [ObservableProperty]
    public partial bool IsCancelable { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [ObservableProperty]
    public partial DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 开始时间
    /// </summary>
    [ObservableProperty]
    public partial DateTimeOffset? StartedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 结束时间
    /// </summary>
    [ObservableProperty]
    public partial DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// 取消任务时执行的委托
    /// </summary>
    public Action? CancelAction { get; set; }

    /// <summary>
    /// 是否可以执行取消命令
    /// </summary>
    public bool CanCancel => IsCancelable && Status == ManagedTaskStatus.Running && CancelAction != null;


    /// <summary>
    /// 取消任务命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CancelAction?.Invoke();
    }

    /// <summary>
    /// 状态变更时刷新派生属性与命令状态
    /// </summary>
    /// <param name="value">新的任务状态</param>
    partial void OnStatusChanged(ManagedTaskStatus value)
    {
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 可取消状态变更时刷新命令可执行性
    /// </summary>
    /// <param name="value">新的可取消状态</param>
    partial void OnIsCancelableChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// 启动托管任务时使用的配置项
/// </summary>
public class ManagedTaskOptions
{
    /// <summary>
    /// 任务标题
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 任务简介
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 任务来源模块
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 是否允许取消
    /// </summary>
    public bool IsCancelable { get; init; }

    /// <summary>
    /// 是否使用不定进度
    /// </summary>
    public bool IsIndeterminate { get; init; } = true;

    /// <summary>
    /// 是否展示进度条
    /// </summary>
    public bool ShowProgress { get; init; } = true;

    /// <summary>
    /// 取消动作回调
    /// </summary>
    public Action? CancelAction { get; init; }
}

/// <summary>
/// 任务中心服务 - 统一维护可取消耗时任务的生命周期与分组集合
/// </summary>
public class TaskCenterService
{
    /// <summary>
    /// 全局单例
    /// </summary>
    public static TaskCenterService Instance { get; } = new();

    /// <summary>
    /// 所有任务总表
    /// </summary>
    public ObservableCollection<ManagedTaskItem> Tasks { get; } = [];

    /// <summary>
    /// 运行中任务集合
    /// </summary>
    public ObservableCollection<ManagedTaskItem> RunningTasks { get; } = [];

    /// <summary>
    /// 已完成任务集合
    /// </summary>
    public ObservableCollection<ManagedTaskItem> CompletedTasks { get; } = [];

    /// <summary>
    /// 已取消任务集合
    /// </summary>
    public ObservableCollection<ManagedTaskItem> CancelledTasks { get; } = [];

    /// <summary>
    /// 失败任务集合
    /// </summary>
    public ObservableCollection<ManagedTaskItem> FailedTasks { get; } = [];

    /// <summary>
    /// 创建并启动一个托管任务
    /// </summary>
    /// <param name="options">任务配置</param>
    /// <returns>创建后的任务实体</returns>
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

    /// <summary>
    /// 更新任务的进度与详情
    /// </summary>
    /// <param name="task">目标任务</param>
    /// <param name="progress">进度值</param>
    /// <param name="detail">详情文本</param>
    /// <param name="isIndeterminate">是否为不定进度</param>
    /// <param name="showProgress">是否展示进度条</param>
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

    /// <summary>
    /// 将任务标记为已完成
    /// </summary>
    /// <param name="task">目标任务</param>
    /// <param name="detail">完成说明</param>
    public void CompleteTask(ManagedTaskItem? task, string? detail = null)
    {
        FinishTask(task, ManagedTaskStatus.Completed, InfoBarSeverity.Success, detail);
    }

    /// <summary>
    /// 将任务标记为已取消
    /// </summary>
    /// <param name="task">目标任务</param>
    /// <param name="detail">取消说明</param>
    public void CancelTask(ManagedTaskItem? task, string? detail = null)
    {
        FinishTask(task, ManagedTaskStatus.Cancelled, InfoBarSeverity.Warning, detail);
    }

    /// <summary>
    /// 将任务标记为失败
    /// </summary>
    /// <param name="task">目标任务</param>
    /// <param name="detail">失败说明</param>
    public void FailTask(ManagedTaskItem? task, string? detail = null)
    {
        FinishTask(task, ManagedTaskStatus.Failed, InfoBarSeverity.Error, detail);
    }

    /// <summary>
    /// 从任务中心删除单条任务
    /// </summary>
    /// <param name="task">目标任务</param>
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

    /// <summary>
    /// 清理历史任务 仅保留运行中项
    /// </summary>
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

    /// <summary>
    /// 统一结束任务并刷新分组
    /// </summary>
    /// <param name="task">目标任务</param>
    /// <param name="status">结束状态</param>
    /// <param name="severity">对应提示级别</param>
    /// <param name="detail">结束说明</param>
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

    /// <summary>
    /// 按标题快速取消虚拟任务 仅用于测试交互
    /// </summary>
    /// <param name="title">任务标题</param>
    /// <param name="detail">取消说明</param>
    private void CancelTaskByTitle(string title, string detail)
    {
        var task = Tasks.FirstOrDefault(x => x.Title == title && x.Status == ManagedTaskStatus.Running);
        if (task != null)
        {
            CancelTask(task, detail);
        }
    }

    /// <summary>
    /// 重建四个任务状态分组
    /// </summary>
    private void RebuildGroups()
    {
        RebuildGroup(RunningTasks, ManagedTaskStatus.Running);
        RebuildGroup(CompletedTasks, ManagedTaskStatus.Completed);
        RebuildGroup(CancelledTasks, ManagedTaskStatus.Cancelled);
        RebuildGroup(FailedTasks, ManagedTaskStatus.Failed);
    }

    /// <summary>
    /// 根据状态刷新目标分组集合
    /// </summary>
    /// <param name="target">目标集合</param>
    /// <param name="status">目标状态</param>
    private void RebuildGroup(ObservableCollection<ManagedTaskItem> target, ManagedTaskStatus status)
    {
        target.Clear();

        foreach (var task in Tasks.Where(x => x.Status == status))
        {
            target.Add(task);
        }
    }
}
