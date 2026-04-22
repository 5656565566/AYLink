using AYLink.Core.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 任务筛选维度枚举 - 用于生成不同类型的任务查询标签页
/// </summary>
public enum TaskFilterKind
{
    Overview,
    Device,
    Status,
    Combined
}

/// <summary>
/// 任务状态筛选项 - 供顶部下拉框绑定
/// </summary>
public class TaskStatusFilterOption
{
    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 对应的任务状态 null 表示全部
    /// </summary>
    public ManagedTaskStatus? Value { get; set; }
}

/// <summary>
/// 任务筛选标签页 ViewModel - 承载一个筛选结果表格
/// </summary>
public partial class TaskCenterTabViewModel : TabItemViewModelBase
{
    /// <summary>
    /// 本地化服务
    /// </summary>
    private readonly LocalizationManager _localizer = LocalizationManager.Instance;

    /// <summary>
    /// 任务中心服务
    /// </summary>
    private readonly TaskCenterService _taskCenterService = TaskCenterService.Instance;

    /// <summary>
    /// 当前标签页筛选结果
    /// </summary>
    public ObservableCollection<ManagedTaskItem> FilteredTasks { get; } = [];

    /// <summary>
    /// 当前选中的任务
    /// </summary>
    [ObservableProperty]
    public partial ManagedTaskItem? SelectedTask { get; set; }

    /// <summary>
    /// 筛选维度
    /// </summary>
    public TaskFilterKind FilterKind { get; }

    /// <summary>
    /// 来源筛选值
    /// </summary>
    public string? SourceFilter { get; }

    /// <summary>
    /// 状态筛选值 null 表示全部
    /// </summary>
    public ManagedTaskStatus? StatusFilter { get; }

    /// <summary>
    /// 名称/来源搜索关键字
    /// </summary>
    public string SearchKeyword { get; }

    /// <summary>
    /// 是否为总览标签页
    /// </summary>
    public bool IsOverview => FilterKind == TaskFilterKind.Overview;

    /// <summary>
    /// 是否存在筛选结果
    /// </summary>
    public bool HasTasks => FilteredTasks.Count > 0;

    /// <summary>
    /// 空状态标题
    /// </summary>
    public string EmptyMessage => IsOverview
        ? _localizer.GetString("TaskCenterPage.EmptyOverview", "当前没有可显示的任务")
        : _localizer.GetString("TaskCenterPage.EmptyFilter", "当前筛选条件下没有匹配任务");

    /// <summary>
    /// 表格状态栏文案
    /// </summary>
    public string SummaryText => string.Format(
        _localizer.GetString("TaskCenterPage.ResultSummary", "共 {0} 条任务"),
        FilteredTasks.Count);


    public TaskCenterTabViewModel(
        string title,
        TaskFilterKind filterKind,
        string? sourceFilter = null,
        ManagedTaskStatus? statusFilter = null,
        string? searchKeyword = null)
    {
        Title = title;
        FilterKind = filterKind;
        SourceFilter = sourceFilter;
        StatusFilter = statusFilter;
        SearchKeyword = searchKeyword ?? string.Empty;

        _taskCenterService.Tasks.CollectionChanged += OnTasksCollectionChanged;

        foreach (var task in _taskCenterService.Tasks)
        {
            SubscribeTask(task);
        }

        RefreshTasks();
    }

    /// <summary>
    /// 按当前筛选条件刷新表格数据
    /// </summary>
    public void RefreshTasks()
    {
        RefreshTasks(_taskCenterService.Tasks.AsEnumerable());
    }

    /// <summary>
    /// 使用指定数据源刷新表格数据
    /// </summary>
    /// <param name="sourceTasks">源任务集合</param>
    public void RefreshTasks(IEnumerable<ManagedTaskItem> sourceTasks)
    {
        var items = sourceTasks;

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            items = items.Where(x =>
                x.Title.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase)
                || x.Source.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SourceFilter))
        {
            items = items.Where(x => string.Equals(x.Source, SourceFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (StatusFilter.HasValue)
        {
            items = items.Where(x => x.Status == StatusFilter.Value);
        }

        var orderedItems = items
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        // 增量同步，避免全量重建导致 UI 闪烁
        for (int i = FilteredTasks.Count - 1; i >= 0; i--)
        {
            if (!orderedItems.Contains(FilteredTasks[i]))
            {
                FilteredTasks.RemoveAt(i);
            }
        }

        for (int i = 0; i < orderedItems.Count; i++)
        {
            var targetItem = orderedItems[i];
            if (i >= FilteredTasks.Count)
            {
                FilteredTasks.Add(targetItem);
            }
            else if (!ReferenceEquals(FilteredTasks[i], targetItem))
            {
                var existingIndex = FilteredTasks.IndexOf(targetItem);
                if (existingIndex > i)
                {
                    FilteredTasks.Move(existingIndex, i);
                }
                else
                {
                    FilteredTasks.Insert(i, targetItem);
                }
            }
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// 刷新派生状态属性
    /// </summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>
    /// 从任务中心移除任务
    /// </summary>
    /// <param name="task">目标任务</param>
    [RelayCommand]
    private void RemoveTask(ManagedTaskItem? task)
    {
        _taskCenterService.RemoveTask(task);
    }

    /// <summary>
    /// 取消运行中的任务
    /// </summary>
    /// <param name="task">目标任务</param>
    [RelayCommand]
    private void CancelTask(ManagedTaskItem? task)
    {
        if (task?.CanCancel == true)
        {
            task.CancelCommand.Execute(null);
        }
    }

    /// <summary>
    /// 清空当前标签页中的未活跃任务
    /// </summary>
    [RelayCommand]
    private void ClearInactiveTasks()
    {
        var inactiveTasks = FilteredTasks
            .Where(x => x.Status != ManagedTaskStatus.Running)
            .ToList();

        foreach (var task in inactiveTasks)
        {
            _taskCenterService.RemoveTask(task);
        }
    }

    /// <summary>
    /// 将选中任务的来源写入顶部筛选栏
    /// </summary>
    /// <param name="task">当前选中的任务</param>
    [RelayCommand]
    private void FillSourceFilter(ManagedTaskItem? task)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.Source))
        {
            return;
        }

        TaskCenterPageViewModel.ApplyPendingSourceFilter(task.Source);
        TaskCenterPageViewModel.TriggerSearch();
    }

    /// <summary>
    /// 将选中任务的状态写入顶部筛选栏
    /// </summary>
    /// <param name="task">当前选中的任务</param>
    [RelayCommand]
    private void FillStatusFilter(ManagedTaskItem? task)
    {
        if (task == null)
        {
            return;
        }

        TaskCenterPageViewModel.ApplyPendingStatusFilter(task.Status);
        TaskCenterPageViewModel.TriggerSearch();
    }


    /// <summary>
    /// 关闭标签页时解除事件订阅
    /// </summary>
    protected override void CloseTab()
    {
        _taskCenterService.Tasks.CollectionChanged -= OnTasksCollectionChanged;

        foreach (var task in _taskCenterService.Tasks)
        {
            UnsubscribeTask(task);
        }

        base.CloseTab();
    }

    /// <summary>
    /// 任务总表变化后同步刷新筛选结果
    /// </summary>
    private void OnTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (ManagedTaskItem item in e.NewItems)
            {
                SubscribeTask(item);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (ManagedTaskItem item in e.OldItems)
            {
                UnsubscribeTask(item);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RefreshTasks();
            return;
        }

        _ = RefreshTasksDeferredAsync();
    }

    private async Task RefreshTasksDeferredAsync()
    {
        await Task.Yield();
        RefreshTasks();
    }

    private void SubscribeTask(ManagedTaskItem task)
    {
        task.PropertyChanged += OnTaskPropertyChanged;
    }

    private void UnsubscribeTask(ManagedTaskItem task)
    {
        task.PropertyChanged -= OnTaskPropertyChanged;
    }

    private void OnTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManagedTaskItem.Status)
            or nameof(ManagedTaskItem.Detail)
            or nameof(ManagedTaskItem.Progress)
            or nameof(ManagedTaskItem.IsIndeterminate)
            or nameof(ManagedTaskItem.IsCancelable)
            or nameof(ManagedTaskItem.Source)
            or nameof(ManagedTaskItem.Title))
        {
            _ = RefreshTasksDeferredAsync();
        }
    }
}

/// <summary>
/// 任务管理页导航参数 - 用于按筛选条件创建新标签页或回填筛选栏
/// </summary>
public class TaskCenterNavigationArgs : NavigationArgs
{
    /// <summary>
    /// 筛选类型
    /// </summary>
    public TaskFilterKind FilterKind { get; set; } = TaskFilterKind.Overview;

    /// <summary>
    /// 来源筛选值
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// 状态筛选值
    /// </summary>
    public ManagedTaskStatus? Status { get; set; }

    /// <summary>
    /// 搜索关键字
    /// </summary>
    public string? SearchKeyword { get; set; }

    /// <summary>
    /// 回填来源输入框
    /// </summary>
    public string? FillSource { get; set; }

    /// <summary>
    /// 回填状态下拉框
    /// </summary>
    public ManagedTaskStatus? FillStatus { get; set; }
}

/// <summary>
/// 任务管理页 ViewModel - 使用标签页承载总览与筛选结果表格
/// </summary>
public partial class TaskCenterPageViewModel : TabbedPageViewModelBase<TaskCenterTabViewModel>
{
    /// <summary>
    /// 本地化服务
    /// </summary>
    private readonly LocalizationManager _localizer = LocalizationManager.Instance;

    /// <summary>
    /// 任务中心服务
    /// </summary>
    private readonly TaskCenterService _taskCenterService = TaskCenterService.Instance;

    /// <summary>
    /// 顶部搜索关键字
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// 顶部来源筛选输入
    /// </summary>
    [ObservableProperty]
    public partial string SourceFilterText { get; set; } = string.Empty;

    /// <summary>
    /// 状态筛选下拉选中项
    /// </summary>
    [ObservableProperty]
    public partial TaskStatusFilterOption? SelectedStatusFilter { get; set; }

    /// <summary>
    /// 状态下拉选项集合
    /// </summary>
    public ObservableCollection<TaskStatusFilterOption> StatusFilterOptions { get; } = [];

    /// <summary>
    /// 页面唯一标识
    /// </summary>
    public override string PageKey => "TaskCenter";

    /// <summary>
    /// 页面标题
    /// </summary>
    public override string Title => _localizer.GetString("TaskCenterPage.Title", "任务管理");

    /// <summary>
    /// 空状态图标
    /// </summary>
    public override string EmptyStateIcon => "Clock";

    /// <summary>
    /// 空状态标题
    /// </summary>
    public override string EmptyStateTitle => _localizer.GetString("TaskCenterPage.EmptyStateTitle", "没有任务标签页");

    /// <summary>
    /// 空状态描述
    /// </summary>
    public override string EmptyStateDescription => _localizer.GetString("TaskCenterPage.EmptyStateDescription", "请通过总览页或筛选功能创建任务查询标签页");

    /// <summary>
    /// 任务管理页允许手动关闭筛选标签页
    /// </summary>
    public override bool IsAddTabButtonVisible => false;

    private static string? _pendingSourceFilter;
    private static ManagedTaskStatus? _pendingStatusFilter;
    private static Action? _triggerSearchAction;

    public TaskCenterPageViewModel()
    {
        if (Avalonia.Controls.Design.IsDesignMode)
        {
        }

        InitializeStatusFilterOptions();

        var overviewTab = CreateOverviewTab();
        overviewTab.IsClosable = false;
        RegisterTab(overviewTab);

        _triggerSearchAction = () =>
        {
            if (!string.IsNullOrWhiteSpace(_pendingSourceFilter))
            {
                SourceFilterText = _pendingSourceFilter;
                SearchText = _pendingSourceFilter;
                _pendingSourceFilter = null;
            }

            if (_pendingStatusFilter.HasValue)
            {
                SelectedStatusFilter = StatusFilterOptions.FirstOrDefault(x => x.Value == _pendingStatusFilter.Value) ?? SelectedStatusFilter;
                _pendingStatusFilter = null;
            }

            SearchCommand.Execute(null);
        };
    }

    /// <summary>
    /// 触发页面级搜索命令
    /// </summary>
    public static void TriggerSearch()
    {
        _triggerSearchAction?.Invoke();
    }

    /// <summary>
    /// TaskCenter 不基于设备直接创建标签页 这里只做占位实现
    /// </summary>
    /// <param name="device">设备模型</param>
    /// <returns>总览标签页</returns>
    protected override TaskCenterTabViewModel CreateTab(DeviceModel device)
    {
        return CreateOverviewTab();
    }

    /// <summary>
    /// 页面导航到时 允许根据筛选参数创建新标签页或回填顶部筛选栏
    /// </summary>
    /// <param name="parameter">导航参数</param>
    public override void OnNavigatedTo(object? parameter = null)
    {
        if (!string.IsNullOrWhiteSpace(_pendingSourceFilter))
        {
            SourceFilterText = _pendingSourceFilter;
            _pendingSourceFilter = null;
        }

        if (_pendingStatusFilter.HasValue)
        {
            SelectedStatusFilter = StatusFilterOptions.FirstOrDefault(x => x.Value == _pendingStatusFilter.Value) ?? SelectedStatusFilter;
            _pendingStatusFilter = null;
        }

        if (parameter is TaskCenterNavigationArgs args)
        {
            if (args.FilterKind != TaskFilterKind.Overview)
            {
                OpenFilterTab(args);
                IsActive = true;
                return;
            }
        }

        base.OnNavigatedTo(parameter);
    }

    /// <summary>
    /// 搜索命令 - 将当前顶部条件直接应用到总览标签页
    /// </summary>
    [RelayCommand]
    private void Search()
    {
        ApplyFiltersToOverviewTab();
    }

    /// <summary>
    /// 根据顶部筛选条件创建新标签页
    /// </summary>
    [RelayCommand]
    private void ApplyFilter()
    {
        var statusValue = SelectedStatusFilter?.Value;
        var sourceValue = string.IsNullOrWhiteSpace(SourceFilterText) ? null : SourceFilterText.Trim();
        var keyword = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

        if (statusValue == null && string.IsNullOrWhiteSpace(sourceValue) && string.IsNullOrWhiteSpace(keyword))
        {
            ApplyFiltersToOverviewTab();
            return;
        }

        Navigation.NavigateTo("TaskCenter", new TaskCenterNavigationArgs
        {
            FilterKind = TaskFilterKind.Combined,
            Source = sourceValue,
            Status = statusValue,
            SearchKeyword = keyword
        });
    }

    /// <summary>
    /// 刷新当前任务页所有标签数据 并同步总览标签的顶部筛选条件
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        ApplyFiltersToOverviewTab();

        foreach (var tab in Tabs.Where(x => !ReferenceEquals(x, Tabs.FirstOrDefault())))
        {
            tab.RefreshTasks();
        }
    }

    /// <summary>
    /// 清理历史任务命令
    /// </summary>
    [RelayCommand]
    private void ClearHistory()
    {
        _taskCenterService.ClearHistory();
        foreach (var tab in Tabs)
        {
            tab.RefreshTasks();
        }
    }

    /// <summary>
    /// 初始化状态筛选下拉选项
    /// </summary>
    private void InitializeStatusFilterOptions()
    {
        StatusFilterOptions.Clear();
        StatusFilterOptions.Add(new TaskStatusFilterOption
        {
            DisplayName = _localizer.GetString("TaskCenterPage.StatusAll", "全部"),
            Value = null
        });
        
        var statuses = new[]
        {
            ManagedTaskStatus.Running,
            ManagedTaskStatus.Completed,
            ManagedTaskStatus.Cancelled,
            ManagedTaskStatus.Failed
        };

        foreach (var status in statuses)
        {
            StatusFilterOptions.Add(new TaskStatusFilterOption
            {
                DisplayName = status.ToLocalizedString(),
                Value = status
            });
        }

        SelectedStatusFilter = StatusFilterOptions.FirstOrDefault();
    }

    /// <summary>
    /// 创建总览标签页
    /// </summary>
    /// <returns>总览标签页实例</returns>
    private TaskCenterTabViewModel CreateOverviewTab()
    {
        return new TaskCenterTabViewModel(
            _localizer.GetString("TaskCenterPage.OverviewTab", "总览"),
            TaskFilterKind.Overview,
            searchKeyword: SearchText);
    }

    /// <summary>
    /// 将当前顶部筛选条件应用到总览标签页
    /// </summary>
    private void ApplyFiltersToOverviewTab()
    {
        if (Tabs.FirstOrDefault() is not TaskCenterTabViewModel overviewTab)
        {
            return;
        }

        var filteredItems = _taskCenterService.Tasks.AsEnumerable();
        var keyword = SearchText?.Trim() ?? string.Empty;
        var statusValue = SelectedStatusFilter?.Value;

        overviewTab.RefreshTasks(filteredItems.Where(x =>
            (string.IsNullOrWhiteSpace(keyword)
             || x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
             || x.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            && (!statusValue.HasValue || x.Status == statusValue.Value)));

        SelectedTab = overviewTab;
    }

    /// <summary>
    /// 根据筛选条件打开标签页 已存在则直接切换
    /// </summary>
    /// <param name="args">筛选导航参数</param>
    private void OpenFilterTab(TaskCenterNavigationArgs args)
    {
        var existing = Tabs.FirstOrDefault(tab =>
            tab.FilterKind == args.FilterKind
            && string.Equals(tab.SourceFilter, args.Source, StringComparison.OrdinalIgnoreCase)
            && tab.StatusFilter == args.Status
            && string.Equals(tab.SearchKeyword, args.SearchKeyword ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.RefreshTasks();
            SelectedTab = existing;
            return;
        }

        var newTab = args.FilterKind switch
        {
            TaskFilterKind.Device => new TaskCenterTabViewModel(
                string.Format(_localizer.GetString("TaskCenterPage.DeviceTabTitle", "来源：{0}"), args.Source ?? _localizer.GetString("TaskCenterPage.UnknownSource", "未知来源")),
                TaskFilterKind.Device,
                args.Source,
                args.Status,
                args.SearchKeyword),
            TaskFilterKind.Status when args.Status.HasValue => new TaskCenterTabViewModel(
                string.Format(_localizer.GetString("TaskCenterPage.StatusTabTitle", "状态：{0}"), args.Status.Value.ToLocalizedString()),
                TaskFilterKind.Status,
                null,
                args.Status.Value,
                args.SearchKeyword),
            TaskFilterKind.Combined => new TaskCenterTabViewModel(
                BuildCombinedTabTitle(args.Source, args.Status, args.SearchKeyword),
                TaskFilterKind.Combined,
                args.Source,
                args.Status,
                args.SearchKeyword),
            _ => CreateOverviewTab()
        };

        RegisterTab(newTab);
    }

    /// <summary>
    /// 构建组合筛选标签标题
    /// </summary>
    /// <param name="source">来源</param>
    /// <param name="status">状态</param>
    /// <param name="keyword">搜索关键字</param>
    /// <returns>标签标题</returns>
    private string BuildCombinedTabTitle(string? source, ManagedTaskStatus? status, string? keyword)
    {
        var parts = new Collection<string>();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            parts.Add(string.Format(_localizer.GetString("TaskCenterPage.SearchTabPart", "搜索：{0}"), keyword));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            parts.Add(string.Format(_localizer.GetString("TaskCenterPage.SourceTabPart", "来源：{0}"), source));
        }

        if (status.HasValue)
        {
            parts.Add(string.Format(_localizer.GetString("TaskCenterPage.StatusTabPart", "状态：{0}"), status.Value.ToLocalizedString()));
        }

        return parts.Count > 0
            ? string.Join(" / ", parts)
            : _localizer.GetString("TaskCenterPage.OverviewTab", "总览");
    }

    /// <summary>
    /// 设置待应用的来源筛选值
    /// </summary>
    /// <param name="source">来源</param>
    public static void ApplyPendingSourceFilter(string source)
    {
        _pendingSourceFilter = source;
    }

    /// <summary>
    /// 设置待应用的状态筛选值
    /// </summary>
    /// <param name="status">状态</param>
    public static void ApplyPendingStatusFilter(ManagedTaskStatus status)
    {
        _pendingStatusFilter = status;
    }

}
