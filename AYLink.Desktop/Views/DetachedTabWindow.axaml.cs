using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AYLink.Desktop.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.Views;

/// <summary>
/// 脱离的标签页窗口 - 支持混合不同类型的标签页
/// 类似浏览器将标签页拖出成独立窗口的功能
///
/// 同步主窗口的毛玻璃/背景图设置（随机模式下每个子窗口独立随机）
/// 支持将标签页拖回主窗口或其他子窗口对应类型的 TabbedPageView
/// </summary>
public partial class DetachedTabWindow : Window
{
    #region Styled Properties

    /// <summary>
    /// 默认内容模板（StyledProperty 以支持 AXAML 绑定）
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> DefaultContentTemplateProperty =
        AvaloniaProperty.Register<DetachedTabWindow, IDataTemplate?>(nameof(DefaultContentTemplate));

    public IDataTemplate? DefaultContentTemplate
    {
        get => GetValue(DefaultContentTemplateProperty);
        set => SetValue(DefaultContentTemplateProperty, value);
    }

    #endregion

    /// <summary>
    /// 独立窗口中的标签页集合（支持混合类型）
    /// </summary>
    public ObservableCollection<TabItemViewModelBase> Tabs { get; } = [];

    /// <summary>
    /// 内容模板选择器 - 根据 ViewModel 类型选择不同的 DataTemplate
    /// </summary>
    public TabContentTemplateSelector ContentTemplateSelector { get; } = new();

    /// <summary>
    /// 源页面 Key - 记录标签页从哪个页面脱离（用于拖回时匹配）
    /// 如 "Screen"、"File" 等
    /// </summary>
    public string? SourcePageKey { get; set; }

    /// <summary>
    /// 顶部拖拽区域的高度
    /// </summary>
    private const double TitleBarHeight = 40;

    private readonly TabDragBehavior _tabDrag;

    public DetachedTabWindow()
    {
        InitializeComponent();

        Services.AppWindowActivationService.Instance.Register(this);

        // 同步主窗口的亚克力/背景图设置
        SyncWithMainWindowAppearance();

        // 绑定 TabView
        PART_TabView.TabItemsSource = Tabs;

        // 窗口拖拽支持
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);

        _tabDrag = new TabDragBehavior(this, PART_TabView, GetDragHost);

        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 同步主窗口的毛玻璃/背景图设置
    /// 如果是随机背景图模式，每个子窗口都随机选择一张新的背景图
    /// </summary>
    private void SyncWithMainWindowAppearance()
    {
        var config = Services.ConfigManager.Instance.LoadConfig<Models.AppConfig>(Models.AppConfig.ConfigName);

        if (config.EnableAcrylic)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur];
            Background = Avalonia.Media.Brushes.Transparent;

            if (!config.EnableBackgroundImage)
            {
                BackgroundMask.Opacity = 0.4;
            }
            else
            {
                BackgroundMask.Opacity = 0.75;
            }
        }
        else
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            Background = Application.Current?.FindResource("SolidBackgroundFillColorBaseBrush") as Avalonia.Media.IBrush
                         ?? Avalonia.Media.Brushes.White;
            BackgroundMask.Opacity = 1.0;
        }

        // 处理背景图
        if (config.EnableBackgroundImage)
        {
            // 注册背景图组件
            Services.BackgroundImageManager.Instance.RegisterImageComponent(GlobalBackgroundImage);

            if (config.BackgroundImageMode == "Random")
            {
                // 随机模式 每个子窗口随机选择一张新背景图
                var images = Services.BackgroundImageManager.Instance.ListBackgroundImages();
                if (images.Count > 0)
                {
                    var random = new Random();
                    var randomImage = images[random.Next(images.Count)];
                    try
                    {
                        GlobalBackgroundImage.Source = new Avalonia.Media.Imaging.Bitmap(randomImage);
                        GlobalBackgroundImage.IsVisible = true;
                    }
                    catch
                    {
                        GlobalBackgroundImage.Source = null;
                        GlobalBackgroundImage.IsVisible = false;
                    }
                }
            }
            // 非随机模式下 RegisterImageComponent 已经会自动应用当前背景图
        }
    }

    /// <summary>
    /// 添加标签页到独立窗口 携带其内容模板
    /// </summary>
    public void AddTab(TabItemViewModelBase tabVm, IDataTemplate? contentTemplate = null)
    {
        AttachTab(tabVm, Tabs.Count, contentTemplate);
    }

    private bool AttachTab(TabItemViewModelBase tabVm, int index, IDataTemplate? contentTemplate)
    {
        if (Tabs.Contains(tabVm)) return false;

        // 订阅关闭事件
        tabVm.OnCloseRequested += OnTabCloseRequested;
        if (tabVm is ITabLifecycleAware lifecycleAware)
            lifecycleAware.OnAttachedToHost();

        // 注册此 ViewModel 类型的内容模板
        if (contentTemplate != null)
        {
            ContentTemplateSelector.Register(tabVm.GetType(), contentTemplate);
            DefaultContentTemplate ??= contentTemplate;
        }

        Tabs.Insert(index, tabVm);
        PART_TabView.SelectedItem = tabVm;
        return true;
    }

    /// <summary>
    /// 从独立窗口移除标签页（不触发关闭清理）
    /// </summary>
    public bool RemoveTab(TabItemViewModelBase tabVm)
    {
        var result = DetachTab(tabVm);
        if (result) CloseIfEmpty();
        return result;
    }

    private bool DetachTab(TabItemViewModelBase tabVm)
    {
        var index = Tabs.IndexOf(tabVm);
        if (index < 0) return false;

        tabVm.OnCloseRequested -= OnTabCloseRequested;
        if (tabVm is ITabLifecycleAware lifecycleAware)
            lifecycleAware.OnDetachedFromHost();
        Tabs.RemoveAt(index);
        if (Tabs.Count > 0 && (PART_TabView.SelectedItem == tabVm || PART_TabView.SelectedItem == null))
            PART_TabView.SelectedItem = Tabs[Math.Min(index, Tabs.Count - 1)];
        return true;
    }

    private void CloseIfEmpty()
    {
        if (Tabs.Count == 0)
            Close();
    }

    private bool MoveTab(TabItemViewModelBase tabVm, int index)
    {
        var oldIndex = Tabs.IndexOf(tabVm);
        if (oldIndex < 0 || index < 0 || index >= Tabs.Count || !tabVm.IsClosable) return false;
        for (var i = Math.Min(oldIndex, index); i <= Math.Max(oldIndex, index); i++)
        {
            if (!Tabs[i].IsClosable) return false;
        }
        if (oldIndex != index) Tabs.Move(oldIndex, index);
        return true;
    }

    internal TabDragHost GetDragHost() => new(this, Tabs, SourcePageKey, DefaultContentTemplate, PART_TabView,
        MoveTab, DetachTab, AttachTab, CloseIfEmpty);

    private void TabView_TabCloseRequested(FATabView _, FATabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabItemViewModelBase tabVm)
        {
            tabVm.CloseTabCommand.Execute(null);
        }
        else if (args.Tab?.DataContext is TabItemViewModelBase tabVm2)
        {
            tabVm2.CloseTabCommand.Execute(null);
        }
    }

    private async void OnTabCloseRequested(TabItemViewModelBase tab)
    {
        if (!DetachTab(tab)) return;
        await TabShutdownHelper.StopAndDisposeAsync(tab);

        if (Tabs.Count == 0)
        {
            Close();
        }
    }

    private async void OnWindowClosed(object? sender, EventArgs e)
    {
        Services.AppWindowActivationService.Instance.Unregister(this);

        // 先从管理器注销 防止其他窗口在关闭过程中尝试与此窗口交互
        DetachedTabWindowManager.Instance.Unregister(this);

        // 取消注册背景图组件
        Services.BackgroundImageManager.Instance.UnregisterImageComponent(GlobalBackgroundImage);

        // 复制集合防止在迭代中修改
        var tabsCopy = Tabs.ToArray();
        Tabs.Clear();

        foreach (var tab in tabsCopy)
        {
            tab.OnCloseRequested -= OnTabCloseRequested;
            try
            {
                if (tab is ITabLifecycleAware lifecycleAware)
                    lifecycleAware.OnDetachedFromHost();
                await TabShutdownHelper.StopAndDisposeAsync(tab);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DetachedTabWindow] Dispose tab error: {ex.Message}");
            }
        }
    }

    #region 窗口标题栏拖拽

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var pos = e.GetPosition(this);
        if (pos.Y > TitleBarHeight)
            return;

        if (e.Source is Visual sourceVisual && IsInteractiveControl(sourceVisual))
            return;

        e.Handled = true;
        BeginMoveDrag(e);
    }

    private static bool IsInteractiveControl(Visual visual)
    {
        var current = visual as Control;
        while (current != null)
        {
            if (current is Button ||
                current is TextBox ||
                current is ComboBox ||
                current is FATabViewListView ||
                current is TabItem)
            {
                return true;
            }
            current = current.Parent as Control;
        }
        return false;
    }

    #endregion
}
