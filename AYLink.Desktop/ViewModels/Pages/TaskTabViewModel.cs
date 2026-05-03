using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Tasks;
using AYLink.Desktop.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AYLink.Desktop.ViewModels.Pages;

public enum TaskFilterKind
{
    Overview,
    NewTab
}

public sealed class TaskFilterDefinition(string? searchKeyword = null, TaskItemStatus? statusFilter = null)
{
    public string SearchKeyword { get; } = searchKeyword?.Trim() ?? string.Empty;
    public TaskItemStatus? StatusFilter { get; } = statusFilter;
    public bool HasCriteria => !string.IsNullOrWhiteSpace(SearchKeyword) || StatusFilter.HasValue;
}

public class TaskStatusFilterOption
{
    public string DisplayName { get; set; } = string.Empty;
    public TaskItemStatus? Value { get; set; }
}

public partial class TaskTabViewModel : TabItemViewModelBase, ITabLifecycleAware
{
    private static readonly HashSet<string> RefreshRelevantProperties =
    [
        nameof(TaskItem.Status),
        nameof(TaskItem.IsCancelable),
        nameof(TaskItem.Source),
        nameof(TaskItem.Title),
        nameof(TaskItem.CreatedAt),
        nameof(TaskItem.FinishedAt)
    ];

    private readonly LocalizationManager _localizer = LocalizationManager.Instance;
    private readonly TaskService _taskService = TaskService.Instance;
    private readonly IUiDispatcher _uiDispatcher = UiDispatcher.Instance;
    private TaskFilterDefinition _activeFilterDefinition = new();
    private bool _refreshPending;
    private bool _isAttachedToHost;
    private bool _disposed;

    public ObservableCollection<TaskItem> FilteredTasks { get; } = [];

    [ObservableProperty]
    public partial TaskItem? SelectedTask { get; set; }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                UpdateActiveFilter();
            }
        }
    }

    private TaskStatusFilterOption? _selectedStatusFilter;
    public TaskStatusFilterOption? SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                UpdateActiveFilter();
            }
        }
    }

    public ObservableCollection<TaskStatusFilterOption> StatusFilterOptions { get; } = [];

    public TaskFilterKind FilterKind { get; }

    public TaskFilterDefinition ActiveFilterDefinition
    {
        get => _activeFilterDefinition;
        private set
        {
            if (SetProperty(ref _activeFilterDefinition, value))
            {
                RequestRefreshTasks(forceImmediate: true);
                OnPropertyChanged(nameof(FilterDescription));
            }
        }
    }

    public bool IsOverview => FilterKind == TaskFilterKind.Overview;
    public bool HasTasks => FilteredTasks.Count > 0;

    public string EmptyMessage => IsOverview
        ? _localizer.GetString("TaskPage.EmptyOverview", "当前没有可显示的任务")
        : _localizer.GetString("TaskPage.EmptyFilter", "当前筛选条件下没有匹配任务");

    public string SummaryText => string.Format(
        _localizer.GetString("TaskPage.ResultSummary", "共 {0} 条任务"),
        FilteredTasks.Count);

    public string FilterDescription => !ActiveFilterDefinition.HasCriteria
        ? _localizer.GetString("TaskPage.FilterNewTabAll", "全部任务")
        : string.Join(" · ", GetFilterDescriptionParts());

    public event System.Action<TaskFilterDefinition>? NewTabRequested;

    public TaskTabViewModel(
        string title,
        TaskFilterKind filterKind,
        TaskFilterDefinition? filterDefinition = null)
    {
        Title = title;
        FilterKind = filterKind;
        
        InitializeStatusFilterOptions();

        if (filterDefinition != null)
        {
            _searchText = filterDefinition.SearchKeyword;
            _selectedStatusFilter = StatusFilterOptions.FirstOrDefault(x => x.Value == filterDefinition.StatusFilter) ?? StatusFilterOptions.FirstOrDefault();
        }
        else
        {
            _selectedStatusFilter = StatusFilterOptions.FirstOrDefault();
        }

        ActiveFilterDefinition = filterDefinition ?? new TaskFilterDefinition();
    }

    public void OnAttachedToHost()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isAttachedToHost)
        {
            return;
        }

        _isAttachedToHost = true;
        _taskService.Items.CollectionChanged += OnTasksCollectionChanged;

        foreach (var task in _taskService.Items)
        {
            SubscribeTask(task);
        }

        RequestRefreshTasks(forceImmediate: true);
    }

    public void OnDetachedFromHost()
    {
        if (!_isAttachedToHost)
        {
            return;
        }

        _isAttachedToHost = false;
        _refreshPending = false;
        _taskService.Items.CollectionChanged -= OnTasksCollectionChanged;

        foreach (var task in _taskService.Items)
        {
            UnsubscribeTask(task);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        OnDetachedFromHost();
        _disposed = true;
        FilteredTasks.Clear();
        SelectedTask = null;
    }

    private void InitializeStatusFilterOptions()
    {
        StatusFilterOptions.Clear();
        StatusFilterOptions.Add(new TaskStatusFilterOption
        {
            DisplayName = _localizer.GetString("TaskPage.StatusAll", "全部"),
            Value = null
        });

        var statuses = new[]
        {
            TaskItemStatus.Running,
            TaskItemStatus.Completed,
            TaskItemStatus.Cancelled,
            TaskItemStatus.Failed
        };

        foreach (var status in statuses)
        {
            StatusFilterOptions.Add(new TaskStatusFilterOption
            {
                DisplayName = status.ToLocalizedString(),
                Value = status
            });
        }
    }

    private void UpdateActiveFilter()
    {
        ActiveFilterDefinition = new TaskFilterDefinition(SearchText, SelectedStatusFilter?.Value);
    }

    public void RefreshTasks()
    {
        RefreshTasks(_taskService.Items.AsEnumerable());
    }

    public void RefreshTasks(IEnumerable<TaskItem> sourceTasks)
    {
        var items = ApplyStaticFilters(sourceTasks);

        var orderedItems = items.OrderByDescending(x => x.CreatedAt).ToList();

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

    private IEnumerable<TaskItem> ApplyStaticFilters(IEnumerable<TaskItem> sourceTasks)
    {
        var items = sourceTasks;
        var keyword = ActiveFilterDefinition.SearchKeyword;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            items = items.Where(x =>
                x.Title.Contains(keyword, System.StringComparison.OrdinalIgnoreCase) ||
                x.Source.Contains(keyword, System.StringComparison.OrdinalIgnoreCase));
        }

        if (ActiveFilterDefinition.StatusFilter.HasValue)
        {
            items = items.Where(x => x.Status == ActiveFilterDefinition.StatusFilter.Value);
        }

        return items;
    }

    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(FilterDescription));
    }

    [RelayCommand]
    private void Search()
    {
        UpdateActiveFilter();
    }

    [RelayCommand]
    private void Refresh()
    {
        RequestRefreshTasks(forceImmediate: true);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = StatusFilterOptions.FirstOrDefault(x => x.Value == null) ?? StatusFilterOptions.FirstOrDefault();
    }

    [RelayCommand]
    private void CreateNewTab()
    {
        NewTabRequested?.Invoke(ActiveFilterDefinition);
    }

    [RelayCommand]
    private void RemoveTask(TaskItem? task)
    {
        _taskService.RequestRemove(task);
    }

    [RelayCommand]
    private void CancelTask(TaskItem? task)
    {
        if (task?.CanCancel == true)
        {
            task.CancelCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ClearInactiveTasks()
    {
        var inactiveTasks = FilteredTasks.Where(x => x.Status != TaskItemStatus.Running).ToList();
        foreach (var task in inactiveTasks)
        {
            _taskService.Remove(task);
        }
    }

    [RelayCommand]
    private void FillSourceFilter(TaskItem? task)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.Source)) return;
        SearchText = task.Source;
        SearchCommand.Execute(null);
    }

    [RelayCommand]
    private void FillStatusFilter(TaskItem? task)
    {
        if (task == null) return;
        SelectedStatusFilter = StatusFilterOptions.FirstOrDefault(x => x.Value == task.Status) ?? SelectedStatusFilter;
        SearchCommand.Execute(null);
    }

    protected override void CloseTab()
    {
        base.CloseTab();
    }

    private void OnTasksCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (TaskItem item in e.NewItems) SubscribeTask(item);
        }
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (TaskItem item in e.OldItems) UnsubscribeTask(item);
        }
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            RequestRefreshTasks(forceImmediate: true);
            return;
        }

        RequestRefreshTasks();
    }

    private void RequestRefreshTasks(bool forceImmediate = false)
    {
        if (_disposed || !_isAttachedToHost)
        {
            return;
        }

        if (forceImmediate)
        {
            _refreshPending = false;
            _uiDispatcher.Post(() =>
            {
                if (_disposed || !_isAttachedToHost)
                {
                    return;
                }

                RefreshTasks();
            });
            return;
        }

        if (_refreshPending)
        {
            return;
        }

        _refreshPending = true;
        _uiDispatcher.Post(() =>
        {
            _refreshPending = false;
            if (_disposed || !_isAttachedToHost)
            {
                return;
            }

            RefreshTasks();
        });
    }

    private void SubscribeTask(TaskItem task)
    {
        task.PropertyChanged += OnTaskPropertyChanged;
    }

    private void UnsubscribeTask(TaskItem task)
    {
        task.PropertyChanged -= OnTaskPropertyChanged;
    }

    private void OnTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            RequestRefreshTasks(forceImmediate: true);
            return;
        }

        if (RefreshRelevantProperties.Contains(e.PropertyName))
        {
            RequestRefreshTasks();
            return;
        }

        if (e.PropertyName is nameof(TaskItem.Detail)
            or nameof(TaskItem.Progress)
            or nameof(TaskItem.IsIndeterminate))
        {
            NotifyStateChanged();
        }
    }

    private IEnumerable<string> GetFilterDescriptionParts()
    {
        if (!string.IsNullOrWhiteSpace(ActiveFilterDefinition.SearchKeyword))
        {
            yield return string.Format(
                _localizer.GetString("TaskPage.SearchTabPart", "搜索：{0}"),
                ActiveFilterDefinition.SearchKeyword);
        }

        if (ActiveFilterDefinition.StatusFilter.HasValue)
        {
            yield return string.Format(
                _localizer.GetString("TaskPage.StatusTabPart", "状态：{0}"),
                ActiveFilterDefinition.StatusFilter.Value.ToLocalizedString());
        }
    }
}
