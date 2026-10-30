using Avalonia;
using Avalonia.Controls;
using AYLink.Desktop.ViewModels;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections;
using System.Collections.Specialized;

namespace AYLink.Desktop.Views;

/// <summary>
/// 通用标签页视图控件
/// 提供 TabView + 空状态 + 状态栏 的统一布局
/// 通过 StyledProperty 注入具体的内容模板和状态栏模板
/// </summary>
public partial class TabbedPageView : UserControl
{
    #region 样式元素

    /// <summary>
    /// Tab 内容模板 - 每个标签页内部渲染的 DataTemplate
    /// </summary>
    public static readonly StyledProperty<Avalonia.Controls.Templates.IDataTemplate?> TabContentTemplateProperty =
        AvaloniaProperty.Register<TabbedPageView, Avalonia.Controls.Templates.IDataTemplate?>(nameof(TabContentTemplate));

    public Avalonia.Controls.Templates.IDataTemplate? TabContentTemplate
    {
        get => GetValue(TabContentTemplateProperty);
        set => SetValue(TabContentTemplateProperty, value);
    }

    /// <summary>
    /// 状态栏模板 - 底部状态栏的 DataTemplate（DataContext 为 SelectedTab）
    /// </summary>
    public static readonly StyledProperty<Avalonia.Controls.Templates.IDataTemplate?> StatusBarTemplateProperty =
        AvaloniaProperty.Register<TabbedPageView, Avalonia.Controls.Templates.IDataTemplate?>(nameof(StatusBarTemplate));

    public Avalonia.Controls.Templates.IDataTemplate? StatusBarTemplate
    {
        get => GetValue(StatusBarTemplateProperty);
        set => SetValue(StatusBarTemplateProperty, value);
    }

    /// <summary>
    /// 是否显示底部状态栏（默认 true）
    /// </summary>
    public static readonly StyledProperty<bool> ShowStatusBarProperty =
        AvaloniaProperty.Register<TabbedPageView, bool>(nameof(ShowStatusBar), true);

    public bool ShowStatusBar
    {
        get => GetValue(ShowStatusBarProperty);
        set => SetValue(ShowStatusBarProperty, value);
    }

    /// <summary>
    /// 空状态图标内容
    /// </summary>
    public static readonly StyledProperty<object?> EmptyStateIconProperty =
        AvaloniaProperty.Register<TabbedPageView, object?>(nameof(EmptyStateIcon));

    public object? EmptyStateIcon
    {
        get => GetValue(EmptyStateIconProperty);
        set => SetValue(EmptyStateIconProperty, value);
    }

    /// <summary>
    /// 空标签页的标题文本
    /// </summary>
    public static readonly StyledProperty<string> EmptyTabTitleProperty =
        AvaloniaProperty.Register<TabbedPageView, string>(nameof(EmptyTabTitle), 
            Services.Localization.LocalizationManager.Instance.GetString("TabbedPageView.EmptyTabTitle", "空标签页")
        );

    public string EmptyTabTitle
    {
        get => GetValue(EmptyTabTitleProperty);
        set => SetValue(EmptyTabTitleProperty, value);
    }

    #endregion

    private readonly TabDragBehavior _tabDrag;

    #region 空标签页管理

    /// <summary>
    /// 当前监听的 Tabs 集合（用于取消订阅）
    /// </summary>
    private INotifyCollectionChanged? _currentTabsCollection;

    /// <summary>
    /// 是否正在显示空标签页
    /// </summary>
    private bool _isShowingEmptyTab;

    #endregion

    public TabbedPageView()
    {
        InitializeComponent();

        _tabDrag = new TabDragBehavior(this, PART_TabView, GetDragHost,
            () => _isShowingEmptyTab ? PART_EmptyTabView : PART_TabView);

        // 监听 DataContext 变化来订阅 Tabs 集合
        DataContextChanged += OnDataContextChanged;
    }

    #region 空标签页逻辑

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 取消旧集合的订阅
        _currentTabsCollection?.CollectionChanged -= OnTabsCollectionChanged;
        _currentTabsCollection = null;

        // 订阅新集合
        if (DataContext is PageViewModelBase pageVm)
        {
            // 使用反射获取 Tabs 属性（因为泛型基类）
            var tabsProp = pageVm.GetType().GetProperty("Tabs");
            if (tabsProp?.GetValue(pageVm) is INotifyCollectionChanged tabs)
            {
                _currentTabsCollection = tabs;
                tabs.CollectionChanged += OnTabsCollectionChanged;
            }
        }

        // 初始更新空标签页状态
        UpdateEmptyTabState();
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyTabState();
    }

    /// <summary>
    /// 根据 Tabs 集合状态更新空标签页显示
    /// </summary>
    private void UpdateEmptyTabState()
    {
        if (DataContext is not PageViewModelBase pageVm)
        {
            SetEmptyTabVisible(true);
            return;
        }

        // 通过反射获取 Tabs.Count
        var tabsProp = pageVm.GetType().GetProperty("Tabs");
        var tabs = tabsProp?.GetValue(pageVm);
        var countProp = tabs?.GetType().GetProperty("Count");
        var count = (int)(countProp?.GetValue(tabs) ?? 0);

        SetEmptyTabVisible(count == 0);
    }

    /// <summary>
    /// 设置空标签页是否可见
    /// 当无功能标签页时 显示占位空标签页 TabView 隐藏真实 TabView
    /// 当有功能标签页时 隐藏占位空标签页 TabView 显示真实 TabView
    /// </summary>
    private void SetEmptyTabVisible(bool visible)
    {
        _isShowingEmptyTab = visible;
        PART_EmptyTabView.IsVisible = visible;
        PART_TabView.IsVisible = !visible;
    }

    #endregion

    private static void TabView_TabCloseRequested(FATabView _, FATabViewTabCloseRequestedEventArgs args)
    {
        // FluentAvalonia TabView 的 args.Item 可能是 ViewModel 或 TabViewItem
        if (args.Item is TabItemViewModelBase tabVm)
        {
            tabVm.CloseTabCommand.Execute(null);
        }
        else if (args.Tab?.DataContext is TabItemViewModelBase tabVm2)
        {
            tabVm2.CloseTabCommand.Execute(null);
        }
    }

    private TabDragHost? GetDragHost()
    {
        if (DataContext is not ITabbedPageViewModel vm || DataContext is not PageViewModelBase page || vm.Tabs is not IList tabs)
            return null;

        return new TabDragHost(vm, tabs, page.PageKey, TabContentTemplate, PART_TabView,
            vm.MoveTab, vm.DetachTab, (tab, index, _) => vm.AttachTab(tab, index), () => { });
    }
}
