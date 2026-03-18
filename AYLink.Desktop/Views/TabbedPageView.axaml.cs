using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AYLink.Desktop.ViewModels;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace AYLink.Desktop.Views;

/// <summary>
/// 通用标签页视图控件
/// 提供 TabView + 空状态 + 状态栏 的统一布局
/// 通过 StyledProperty 注入具体的内容模板和状态栏模板
///
/// 支持浏览器风格的标签页脱离功能：
/// - 向左右拖拽一小段距离即可分离
/// - 向上下分离的距离是左右的 2 倍
/// - 脱出标签页松手前鼠标有动画提示
///
/// 空标签页机制：
/// - 当没有功能标签页时，自动显示一个不可关闭的空标签页
/// - 空标签页显示提示信息（图标、标题、描述）
/// - 当添加功能标签页时，空标签页自动消失
/// - 当所有功能标签页关闭后，空标签页重新出现
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
    /// 空状态图标内容（可以是 SymbolIcon、PathIcon 等任意 Control）
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
        AvaloniaProperty.Register<TabbedPageView, string>(nameof(EmptyTabTitle), "新标签页");

    public string EmptyTabTitle
    {
        get => GetValue(EmptyTabTitleProperty);
        set => SetValue(EmptyTabTitleProperty, value);
    }

    #endregion

    #region 拖拽脱离相关字段

    // 拖拽状态
    private TabViewItem? _draggingTabItem;
    private TabItemViewModelBase? _draggingTabVm;
    private Point _dragStartPoint;
    private bool _isDragTracking;
    private bool _isTearingOff;

    // 拖拽阈值（像素）
    private const double DragThreshold = 5;
    /// <summary>
    /// 水平方向脱离阈值（像素）
    /// </summary>
    private const double TearOffThresholdX = 50;
    /// <summary>
    /// 垂直方向脱离阈值（像素）- 上下方向是左右的 2 倍
    /// </summary>
    private const double TearOffThresholdY = 100;

    /// <summary>
    /// 脱离指示动画覆盖层
    /// </summary>
    private Border? _tearOffIndicator;

    #endregion

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

        // 使用隧道策略在 TabView 标签栏区域捕获拖拽事件
        AddHandler(PointerPressedEvent, OnTabAreaPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTabAreaPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTabAreaPointerReleased, RoutingStrategies.Tunnel);

        // 监听 DataContext 变化来订阅 Tabs 集合
        DataContextChanged += OnDataContextChanged;
    }

    #region 空标签页逻辑

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 取消旧集合的订阅
        if (_currentTabsCollection != null)
        {
            _currentTabsCollection.CollectionChanged -= OnTabsCollectionChanged;
            _currentTabsCollection = null;
        }

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

    /// <summary>
    /// 统一的标签页关闭处理 - 调用 TabItemViewModelBase.CloseTabCommand
    /// </summary>
    private void TabView_TabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        if (args.Item is TabItemViewModelBase tabVm && !tabVm.IsClosable)
        {
            args.Cancel = true;
        }
        else if (args.Tab?.DataContext is TabItemViewModelBase tabVm2 && !tabVm2.IsClosable)
        {
            args.Cancel = true;
        }
    }

    private void TabView_TabCloseRequested(TabView _, TabViewTabCloseRequestedEventArgs args)
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

    #region 拖拽脱离逻辑

    /// <summary>
    /// 在 TabView 标签栏区域检测鼠标按下
    /// </summary>
    private void OnTabAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 查找点击目标是否为 TabViewItem
        var tabItem = FindAncestor<TabViewItem>(e.Source as Visual);
        if (tabItem == null) return;

        // 检查是否点击在关闭按钮上
        if (IsClickOnButton(e.Source as Visual, tabItem)) return;

        // 获取 ViewModel
        var tabVm = GetTabViewModel(tabItem);
        if (tabVm == null) return;

        // 如果标签页不可关闭（固定标签页），则不允许脱离
        if (!tabVm.IsClosable) return;

        _draggingTabItem = tabItem;
        _draggingTabVm = tabVm;
        _dragStartPoint = e.GetPosition(PART_TabView);
        _isDragTracking = true;
        _isTearingOff = false;
    }

    /// <summary>
    /// 鼠标移动时检测是否超过脱离阈值
    /// </summary>
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

        // 检测是否满足方向性脱离阈值
        bool shouldTearOff = Math.Abs(diff.X) > TearOffThresholdX || Math.Abs(diff.Y) > TearOffThresholdY;

        if (shouldTearOff && !_isTearingOff)
        {
            _isTearingOff = true;
            ShowTearOffIndicator();
        }
        else if (!shouldTearOff && _isTearingOff)
        {
            _isTearingOff = false;
            HideTearOffIndicator();
        }

        // 更新脱离指示器位置
        if (_isTearingOff)
        {
            UpdateTearOffIndicator(e);
        }
    }

    /// <summary>
    /// 鼠标释放时执行脱离或取消
    /// </summary>
    private void OnTabAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragTracking) return;

        if (_isTearingOff && _draggingTabVm != null)
        {
            // 执行脱离
            PerformTearOff(e);
        }

        HideTearOffIndicator();
        ResetDragState();
    }

    /// <summary>
    /// 执行标签页脱离 - 创建独立窗口
    ///
    /// 关键：必须使用 Dispatcher.UIThread.Post 延迟执行 DetachTab，
    /// 因为 FluentAvalonia 的 TabViewListView 在 PointerReleased 事件链中
    /// 还会调用 EndReorder() 访问集合索引。如果在事件链中同步移除集合项，
    /// 会导致 EndReorder() 中 Index was out of range 的
    /// ArgumentOutOfRangeException 崩溃。
    /// </summary>
    private void PerformTearOff(PointerReleasedEventArgs e)
    {
        if (_draggingTabVm == null) return;

        var tabVm = _draggingTabVm;
        var contentTemplate = TabContentTemplate;
        var pageVm = DataContext as PageViewModelBase;
        if (pageVm == null) return;

        // 获取屏幕坐标用于定位新窗口（在 detach 前获取，因为 detach 后控件可能变化）
        var topLevel = TopLevel.GetTopLevel(this);
        PixelPoint windowPos;
        if (topLevel is Window window)
        {
            var screenPoint = e.GetPosition(window);
            windowPos = new PixelPoint(
                (int)(window.Position.X + screenPoint.X - 200),
                (int)(window.Position.Y + screenPoint.Y - 30));
        }
        else
        {
            windowPos = new PixelPoint(100, 100);
        }

        // 延迟执行：让 FluentAvalonia TabViewListView.EndReorder() 先完成，
        // 然后再从集合中移除标签页并创建分离窗口
        Dispatcher.UIThread.Post(() =>
        {
            // 从源 ViewModel 中移除标签页
            bool detached = TryDetachFromViewModel(pageVm, tabVm);
            if (!detached) return;

            // 创建脱离窗口
            var detachedWindow = new DetachedTabWindow
            {
                DefaultContentTemplate = contentTemplate,
                Position = windowPos,
                SourcePageKey = pageVm.PageKey // 记录源页面 Key 用于拖回时匹配
            };
            detachedWindow.AddTab(tabVm, contentTemplate);

            // 注册到全局管理器
            DetachedTabWindowManager.Instance.Register(detachedWindow);

            // 显示脱离窗口后不设置 Owner 允许独立操作）
            detachedWindow.Show();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 尝试从 ViewModel 中脱离标签页
    /// 使用反射调用 DetachTab 方法，支持所有 TabbedPageViewModelBase&lt;TTab&gt; 的子类
    /// </summary>
    private static bool TryDetachFromViewModel(PageViewModelBase pageVm, TabItemViewModelBase tabVm)
    {
        // 查找 DetachTab 方法
        var method = pageVm.GetType().GetMethod("DetachTab");
        if (method != null)
        {
            var result = method.Invoke(pageVm, [tabVm]);
            return result is true;
        }
        return false;
    }

    #endregion

    #region 脱离指示器（鼠标动画）

    /// <summary>
    /// 显示脱离指示覆盖层 - 在标签页上显示分离动画提示
    /// </summary>
    private void ShowTearOffIndicator()
    {
        if (_draggingTabItem == null) return;

        // 在 TabView 上创建一个浮动指示器
        if (_tearOffIndicator == null)
        {
            _tearOffIndicator = new Border
            {
                Background = new SolidColorBrush(Colors.White, 0.1),
                BorderBrush = new SolidColorBrush(Color.Parse("#60AAAAFF")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false,
                ZIndex = 1000,
                Child = new StackPanel
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "\uE8A7", // Open/Detach icon
                            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                            FontSize = 20,
                            Foreground = Brushes.White,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        },
                    }
                },
                Width = 48,
                Height = 48,
                Opacity = 0,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            };
        }

        // 将指示器添加到最顶层
        var rootGrid = this.FindDescendantOfType<Grid>();
        if (rootGrid != null && !rootGrid.Children.Contains(_tearOffIndicator))
        {
            _tearOffIndicator.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            _tearOffIndicator.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            rootGrid.Children.Add(_tearOffIndicator);
        }

        // 初始缩放
        _tearOffIndicator.RenderTransform = new ScaleTransform(0.5, 0.5);

        // 渐显动画（仅 Opacity）
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1.0),
                    }
                }
            }
        };
        animation.RunAsync(_tearOffIndicator).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_tearOffIndicator != null)
                    _tearOffIndicator.RenderTransform = new ScaleTransform(1.0, 1.0);
            });
        });
    }

    /// <summary>
    /// 隐藏脱离指示器
    /// </summary>
    private void HideTearOffIndicator()
    {
        if (_tearOffIndicator == null) return;

        // 渐隐动画后移除
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(OpacityProperty, 1.0) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(OpacityProperty, 0.0) }
                }
            }
        };

        var indicator = _tearOffIndicator;
        animation.RunAsync(indicator).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var rootGrid = this.FindDescendantOfType<Grid>();
                if (rootGrid != null && rootGrid.Children.Contains(indicator))
                {
                    rootGrid.Children.Remove(indicator);
                }
            });
        });

        _tearOffIndicator = null;
    }

    /// <summary>
    /// 更新脱离指示器位置（跟随鼠标）
    /// </summary>
    private void UpdateTearOffIndicator(PointerEventArgs e)
    {
        if (_tearOffIndicator == null) return;

        var pos = e.GetPosition(this);
        _tearOffIndicator.Margin = new Thickness(
            pos.X - _tearOffIndicator.Width / 2,
            pos.Y - _tearOffIndicator.Height / 2,
            0, 0);
    }

    #endregion

    #region 拖拽透明度修复

    /// <summary>
    /// 当 TabView 加载完成后，修复标签页拖拽时的透明度问题
    /// 通过覆盖 TabViewItem 的 :dragging 伪类样式
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FixTabDragOpacity();
    }

    /// <summary>
    /// 修复 FluentAvalonia TabViewItem 拖拽时完全透明的问题
    /// FluentAvalonia 默认在拖拽重排时将 TabViewItem 设置为 Opacity=0
    /// 这里通过添加样式来覆盖
    /// </summary>
    private void FixTabDragOpacity()
    {
        // 为 TabView 中的 TabViewItem 添加拖拽时的透明度修复样式
        var style = new Style(x =>
            x.OfType<TabViewItem>().Class(":dragging"));
        style.Setters.Add(new Setter(OpacityProperty, 0.7));

        // 拖拽时的缩放效果
        style.Setters.Add(new Setter(
            RenderTransformProperty,
            new ScaleTransform(0.95, 0.95)));

        PART_TabView.Styles.Add(style);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 重置拖拽状态
    /// </summary>
    private void ResetDragState()
    {
        _draggingTabItem = null;
        _draggingTabVm = null;
        _isDragTracking = false;
        _isTearingOff = false;
    }

    /// <summary>
    /// 获取 TabViewItem 对应的 ViewModel
    /// </summary>
    private static TabItemViewModelBase? GetTabViewModel(TabViewItem tabItem)
    {
        if (tabItem.Content is TabItemViewModelBase vm) return vm;
        if (tabItem.DataContext is TabItemViewModelBase vm2) return vm2;
        return null;
    }

    /// <summary>
    /// 在可视化树中向上查找指定类型的祖先
    /// </summary>
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

    /// <summary>
    /// 检查点击是否在按钮元素上（关闭按钮等）
    /// </summary>
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
}
