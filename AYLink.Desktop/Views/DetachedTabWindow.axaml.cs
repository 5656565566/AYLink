using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

    #region 拖拽回主窗口相关字段

    private TabViewItem? _draggingTabItem;
    private TabItemViewModelBase? _draggingTabVm;
    private Point _dragStartPoint;
    private bool _isDragTracking;
    private bool _isTearingOff;
    private const double TearOffThresholdX = 50;
    private const double TearOffThresholdY = 100;

    #endregion

    public DetachedTabWindow()
    {
        InitializeComponent();

        Services.AppWindowActivationService.Instance.Register(this);

        // 同步主窗口的亚克力/背景图设置
        SyncWithMainWindowAppearance();

        // 绑定 TabView
        PART_TabView.TabItems = Tabs;

        // 窗口拖拽支持
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);

        // 标签页拖回支持
        AddHandler(PointerPressedEvent, OnTabAreaPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTabAreaPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTabAreaPointerReleased, RoutingStrategies.Tunnel);

        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 同步主窗口的毛玻璃/背景图设置
    /// 如果是随机背景图模式，每个子窗口都随机选择一张新的背景图
    /// </summary>
    private void SyncWithMainWindowAppearance()
    {
        var config = Services.ConfigManager.Instance.LoadConfig<Models.AppConfig>("appConfig");

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
        // 订阅关闭事件
        tabVm.OnCloseRequested += OnTabCloseRequested;

        // 注册此 ViewModel 类型的内容模板
        if (contentTemplate != null)
        {
            ContentTemplateSelector.Register(tabVm.GetType(), contentTemplate);
            DefaultContentTemplate ??= contentTemplate;
        }

        Tabs.Add(tabVm);
        PART_TabView.SelectedItem = tabVm;
    }

    /// <summary>
    /// 从独立窗口移除标签页（不触发关闭清理）
    /// </summary>
    public bool RemoveTab(TabItemViewModelBase tabVm)
    {
        tabVm.OnCloseRequested -= OnTabCloseRequested;
        var result = Tabs.Remove(tabVm);

        if (Tabs.Count == 0)
        {
            Close();
        }

        return result;
    }

    private void TabView_TabCloseRequested(TabView _, TabViewTabCloseRequestedEventArgs args)
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

    private void OnTabCloseRequested(TabItemViewModelBase tab)
    {
        tab.OnCloseRequested -= OnTabCloseRequested;

        if (tab is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            Close();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
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
            if (tab is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DetachedTabWindow] Dispose tab error: {ex.Message}");
                }
            }
        }
    }

    #region 拖回主窗口逻辑

    /// <summary>
    /// 在 TabView 标签栏区域检测鼠标按下
    /// </summary>
    private void OnTabAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var tabItem = FindAncestor<TabViewItem>(e.Source as Visual);
        if (tabItem == null) return;

        if (IsClickOnButton(e.Source as Visual, tabItem)) return;

        var tabVm = GetTabViewModel(tabItem);
        if (tabVm == null) return;

        // 固定的标签页（不可关闭）不允许拖拽
        if (!tabVm.IsClosable) return;

        _draggingTabItem = tabItem;
        _draggingTabVm = tabVm;
        _dragStartPoint = e.GetPosition(PART_TabView);
        _isDragTracking = true;
        _isTearingOff = false;
    }

    private void OnTabAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragTracking || _draggingTabItem == null || _draggingTabVm == null) return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetDragState();
            return;
        }

        var currentPos = e.GetPosition(PART_TabView);
        var diff = currentPos - _dragStartPoint;

        bool shouldTearOff = Math.Abs(diff.X) > TearOffThresholdX || Math.Abs(diff.Y) > TearOffThresholdY;

        if (shouldTearOff && !_isTearingOff)
        {
            _isTearingOff = true;
        }
        else if (!shouldTearOff && _isTearingOff)
        {
            _isTearingOff = false;
        }
    }

    /// <summary>
    /// 鼠标释放时尝试拖到目标窗口（主窗口或其他子窗口）
    /// </summary>
    private void OnTabAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragTracking) return;

        if (_isTearingOff && _draggingTabVm != null)
        {
            TryDragToTarget(e);
        }

        ResetDragState();
    }

    /// <summary>
    /// 尝试将标签页拖到目标窗口（主窗口或其他子窗口）
    /// </summary>
    private void TryDragToTarget(PointerReleasedEventArgs e)
    {
        if (_draggingTabVm == null || string.IsNullOrEmpty(SourcePageKey)) return;

        var tabVm = _draggingTabVm;
        var sourcePageKey = SourcePageKey;
        var contentTemplate = DefaultContentTemplate;

        // 获取鼠标在屏幕上的位置
        var screenPoint = e.GetPosition(this);
        var screenPixelPoint = new PixelPoint(
            (int)(Position.X + screenPoint.X),
            (int)(Position.Y + screenPoint.Y));

        // 优先检查是否拖到其他子窗口
        var targetDetachedWindow = DetachedTabWindowManager.Instance.FindWindowUnderPoint(screenPixelPoint, this);
        if (targetDetachedWindow != null && targetDetachedWindow.SourcePageKey == sourcePageKey)
        {
            // 拖到同类型的其他子窗口
            Dispatcher.UIThread.Post(() =>
            {
                RemoveTab(tabVm);
                targetDetachedWindow.AddTab(tabVm, contentTemplate);
            }, DispatcherPriority.Background);
            return;
        }

        // 检查是否拖到主窗口
        var mainWindow = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (mainWindow == null) return;

        var mainWindowBounds = new PixelRect(
            mainWindow.Position,
            PixelSize.FromSize(mainWindow.Bounds.Size, mainWindow.RenderScaling));

        if (!mainWindowBounds.Contains(screenPixelPoint)) return;

        // 延迟执行以避免与 FluentAvalonia 重排冲突
        Dispatcher.UIThread.Post(() =>
        {
            // 查找主窗口 ViewModel
            if (mainWindow.DataContext is not MainWindowViewModel mainVm) return;

            // 通过反射获取对应页面的 ViewModel
            var pagesField = mainVm.GetType().GetField("_pages",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (pagesField?.GetValue(mainVm) is not System.Collections.Generic.Dictionary<string, PageViewModelBase> pages) return;

            if (!pages.TryGetValue(sourcePageKey, out var targetPageVm)) return;

            // 尝试 AttachTab（类型匹配由 AttachTab 内部检查）
            var attachMethod = targetPageVm.GetType().GetMethod("AttachTab");
            if (attachMethod == null) return;

            // 先从子窗口中移除标签页
            RemoveTab(tabVm);

            // 再附加到主窗口的页面
            var result = attachMethod.Invoke(targetPageVm, [tabVm, -1]);
            if (result is not true)
            {
                // 附加失败，重新添加回子窗口
                AddTab(tabVm, contentTemplate);
                return;
            }

            // 导航到对应页面
            Services.NavigationService.Instance.NavigateTo(sourcePageKey);
        }, DispatcherPriority.Background);
    }

    private void ResetDragState()
    {
        _draggingTabItem = null;
        _draggingTabVm = null;
        _isDragTracking = false;
        _isTearingOff = false;
    }

    private static TabItemViewModelBase? GetTabViewModel(TabViewItem tabItem)
    {
        if (tabItem.Content is TabItemViewModelBase vm) return vm;
        if (tabItem.DataContext is TabItemViewModelBase vm2) return vm2;
        return null;
    }

    private static T? FindAncestor<T>(Visual? visual) where T : Visual
    {
        var current = visual;
        while (current != null)
        {
            if (current is T target) return target;
            current = current.GetVisualParent() as Visual;
        }
        return null;
    }

    private static bool IsClickOnButton(Visual? source, TabViewItem tabItem)
    {
        var current = source as Control;
        while (current != null && current != tabItem)
        {
            if (current is Button) return true;
            current = current.Parent as Control;
        }
        return false;
    }

    #endregion

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
                current is TabViewListView ||
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
