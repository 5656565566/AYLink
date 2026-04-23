using System.Collections.Generic;
using System.Linq;
using AYLink.Core.Models;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.Services.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class TaskPageViewModel : TabbedPageViewModelBase<TaskTabViewModel>
{
    private readonly LocalizationManager _localizer = LocalizationManager.Instance;

    public override string PageKey => "Task";

    public override string Title => _localizer.GetString("TaskPage.Title", "任务管理");

    public override string EmptyStateIcon => "Clock";

    public override string EmptyStateTitle => _localizer.GetString("TaskPage.EmptyStateTitle", "没有任务标签页");

    public override string EmptyStateDescription => _localizer.GetString("TaskPage.EmptyStateDescription", "请通过总览页或筛选快照创建任务查询标签页");

    public override bool IsAddTabButtonVisible => false;

    public TaskPageViewModel()
    {
        if (Avalonia.Controls.Design.IsDesignMode) return;

        var overviewTab = CreateOverviewTab();
        overviewTab.IsClosable = false;
        RegisterTab(overviewTab);
    }

    private void OnNewTabRequested(TaskFilterDefinition filter)
    {
        var newTab = CreateFilterTab(filter);
        RegisterTab(newTab);
        SelectedTab = newTab;
    }

    private TaskTabViewModel CreateOverviewTab()
    {
        var tab = new TaskTabViewModel(
            _localizer.GetString("TaskPage.OverviewTab", "总览"),
            TaskFilterKind.Overview);
        tab.NewTabRequested += OnNewTabRequested;
        return tab;
    }

    private TaskTabViewModel CreateFilterTab(TaskFilterDefinition filter)
    {
        var tab = new TaskTabViewModel(
            BuildFilterTabTitle(filter),
            TaskFilterKind.NewTab,
            filter);
        tab.NewTabRequested += OnNewTabRequested;
        return tab;
    }

    private string BuildFilterTabTitle(TaskFilterDefinition filter)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword))
        {
            parts.Add(string.Format(
                _localizer.GetString("TaskPage.SearchTabPart", "搜索：{0}"),
                filter.SearchKeyword));
        }

        if (filter.StatusFilter.HasValue)
        {
            parts.Add(string.Format(
                _localizer.GetString("TaskPage.StatusTabPart", "状态：{0}"),
                filter.StatusFilter.Value.ToLocalizedString()));
        }

        if (parts.Count == 0)
        {
            return _localizer.GetString("TaskPage.FilterNewTab", "新标签页");
        }

        return string.Join(" · ", parts);
    }

    protected override void OnTabClosed(TaskTabViewModel tab)
    {
        tab.NewTabRequested -= OnNewTabRequested;
        base.OnTabClosed(tab);
    }

    protected override TaskTabViewModel CreateTab(DeviceModel device)
    {
        return CreateOverviewTab();
    }
}
