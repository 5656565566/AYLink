using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections;
using System.Linq;

namespace AYLink.Controls;

[TemplatePart("PART_ItemsPresenter", typeof(ItemsPresenter))]
[TemplatePart("PART_AddButton", typeof(Button))]
public class BrowserTabView : TabControl
{
    #region Styled Properties

    public static readonly StyledProperty<bool> IsAddButtonVisibleProperty =
        AvaloniaProperty.Register<BrowserTabView, bool>(nameof(IsAddButtonVisible), true);

    public bool IsAddButtonVisible
    {
        get => GetValue(IsAddButtonVisibleProperty);
        set => SetValue(IsAddButtonVisibleProperty, value);
    }

    #endregion

    #region Routed Events

    public static readonly RoutedEvent<RoutedEventArgs> AddTabRequestedEvent =
        RoutedEvent.Register<BrowserTabView, RoutedEventArgs>(nameof(AddTabRequested), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> AddTabRequested
    {
        add => AddHandler(AddTabRequestedEvent, value);
        remove => RemoveHandler(AddTabRequestedEvent, value);
    }

    public static readonly RoutedEvent<TabCloseRequestedEventArgs> TabCloseRequestedEvent =
        RoutedEvent.Register<BrowserTabView, TabCloseRequestedEventArgs>(nameof(TabCloseRequested), RoutingStrategies.Bubble);

    public event EventHandler<TabCloseRequestedEventArgs> TabCloseRequested
    {
        add => AddHandler(TabCloseRequestedEvent, value);
        remove => RemoveHandler(TabCloseRequestedEvent, value);
    }

    public static readonly RoutedEvent<TabDraggedOutsideEventArgs> TabDraggedOutsideEvent =
        RoutedEvent.Register<BrowserTabView, TabDraggedOutsideEventArgs>(nameof(TabDraggedOutside), RoutingStrategies.Bubble);

    public event EventHandler<TabDraggedOutsideEventArgs> TabDraggedOutside
    {
        add => AddHandler(TabDraggedOutsideEvent, value);
        remove => RemoveHandler(TabDraggedOutsideEvent, value);
    }

    #endregion

    #region Private Fields

    private Button? _addButton;
    private ItemsPresenter? _itemsPresenter;
    private AnimatedTabPanel? _animatedPanel;

    // 拖拽状态
    private BrowserTabItem? _draggingTab;
    private Point _dragStartPoint;
    private Point _dragStartTabPosition;
    private bool _isDragging;
    private int _draggingOriginalIndex = -1;

    // 拖拽阈值（像素）
    private const double DragThreshold = 5;
    // 拖出标签栏的 Y 轴阈值（像素）
    private const double TearOffThresholdY = 30;

    #endregion

    #region Container Management

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new BrowserTabItem();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<BrowserTabItem>(item, out recycleKey);
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is BrowserTabItem tabItem)
        {
            tabItem.CloseRequested += OnTabItemCloseRequested;
            tabItem.PointerPressed += OnTabItemPointerPressed;
            tabItem.PointerMoved += OnTabItemPointerMoved;
            tabItem.PointerReleased += OnTabItemPointerReleased;
        }
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);
        if (container is BrowserTabItem tabItem)
        {
            tabItem.CloseRequested -= OnTabItemCloseRequested;
            tabItem.PointerPressed -= OnTabItemPointerPressed;
            tabItem.PointerMoved -= OnTabItemPointerMoved;
            tabItem.PointerReleased -= OnTabItemPointerReleased;
        }
    }

    #endregion

    #region Template

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_addButton != null)
            _addButton.Click -= AddButton_Click;

        _addButton = e.NameScope.Find<Button>("PART_AddButton");
        _itemsPresenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");

        if (_addButton != null)
            _addButton.Click += AddButton_Click;

        // 注册到全局管理器
        TabWindowManager.Instance.Register(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TabWindowManager.Instance.Unregister(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TabWindowManager.Instance.Register(this);
    }

    /// <summary>
    /// 获取内部的 AnimatedTabPanel（供 TabWindowManager 使用）
    /// </summary>
    internal AnimatedTabPanel? GetAnimatedPanel()
    {
        if (_animatedPanel != null) return _animatedPanel;
        _animatedPanel = _itemsPresenter?.Panel as AnimatedTabPanel;
        return _animatedPanel;
    }

    #endregion

    #region Add Button

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(AddTabRequestedEvent, this));
    }

    #endregion

    #region Close Tab

    private void OnTabItemCloseRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is BrowserTabItem tabItem)
        {
            var item = ItemFromContainer(tabItem) ?? tabItem;
            RaiseEvent(new TabCloseRequestedEventArgs(TabCloseRequestedEvent, this, item, tabItem));
        }
    }

    #endregion

    #region Drag - Pointer Events

    private void OnTabItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not BrowserTabItem tabItem) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 检查是否点击在关闭按钮或左侧操作按钮上，如果是则不启动拖拽
        if (IsClickOnButton(e, tabItem)) return;

        _draggingTab = tabItem;
        _dragStartPoint = e.GetPosition(this);
        _dragStartTabPosition = e.GetPosition(tabItem);
        _isDragging = false;
        _draggingOriginalIndex = IndexFromContainer(tabItem);
    }

    private void OnTabItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingTab == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // 鼠标按钮已释放，重置状态
            ResetDragState(e);
            return;
        }

        var currentPoint = e.GetPosition(this);
        var diff = currentPoint - _dragStartPoint;

        // 检查是否超过拖拽阈值
        if (!_isDragging)
        {
            if (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold)
            {
                _isDragging = true;
                _draggingTab.SetDraggingPseudoClass(true);
                e.Pointer.Capture(_draggingTab);

                // 通知 AnimatedTabPanel 开始拖拽
                var panel = GetAnimatedPanel();
                panel?.SetDraggedChild(_draggingTab, _draggingOriginalIndex);
            }
            else
            {
                return;
            }
        }

        // 检查是否拖出标签栏区域（Tear-off）
        if (currentPoint.Y < -TearOffThresholdY || currentPoint.Y > Bounds.Height + TearOffThresholdY)
        {
            HandleDragOutside(e);
            return;
        }

        // 内部拖拽重排
        UpdateDragPosition(diff.X);
    }

    private void OnTabItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingTab == null) return;

        if (_isDragging)
        {
            // 提交重排
            CommitReorder();
            _draggingTab.SetDraggingPseudoClass(false);
            e.Pointer.Capture(null);
        }

        ResetDragState(null);
    }

    #endregion

    #region Drag - Internal Reorder

    /// <summary>
    /// 更新拖拽位置，通过 AnimatedTabPanel 实现平滑让位
    /// </summary>
    private void UpdateDragPosition(double offsetX)
    {
        if (_draggingTab == null) return;

        var panel = GetAnimatedPanel();
        if (panel != null)
        {
            // 使用 AnimatedTabPanel 的拖拽功能
            panel.UpdateDragOffset(offsetX);
        }
        else
        {
            // 回退：直接使用 RenderTransform
            _draggingTab.RenderTransform = new TranslateTransform(offsetX, 0);
            UpdateTabPositionsFallback(offsetX);
        }
    }

    /// <summary>
    /// 提交重排：将拖拽的标签页移动到目标索引
    /// </summary>
    private void CommitReorder()
    {
        if (_draggingTab == null) return;

        var panel = GetAnimatedPanel();
        int newIndex;

        if (panel != null)
        {
            newIndex = panel.GetDragTargetIndex();
            panel.ClearDragState();
        }
        else
        {
            newIndex = CalculateFallbackTargetIndex();
        }

        if (newIndex >= 0 && newIndex != _draggingOriginalIndex && Items is IList list)
        {
            var item = ItemFromContainer(_draggingTab) ?? _draggingTab;
            list.RemoveAt(_draggingOriginalIndex);
            if (newIndex > list.Count) newIndex = list.Count;
            list.Insert(newIndex, item);
            SelectedIndex = newIndex;
        }

        // 清除所有标签的 RenderTransform
        ResetAllTabTransforms();
    }

    /// <summary>
    /// 回退模式下的标签位置更新（当 AnimatedTabPanel 不可用时）
    /// </summary>
    private void UpdateTabPositionsFallback(double currentOffsetX)
    {
        if (_draggingTab == null || _itemsPresenter?.Panel == null) return;

        var panel = _itemsPresenter.Panel;
        var draggingBounds = _draggingTab.Bounds;
        var currentX = _dragStartPoint.X + currentOffsetX;

        foreach (var container in panel.Children)
        {
            if (container is BrowserTabItem tab && tab != _draggingTab)
            {
                var index = IndexFromContainer(tab);
                var bounds = tab.Bounds;
                var center = bounds.X + bounds.Width / 2;

                if (_draggingOriginalIndex < index && currentX > center)
                {
                    tab.RenderTransform = new TranslateTransform(-draggingBounds.Width, 0);
                }
                else if (_draggingOriginalIndex > index && currentX < center)
                {
                    tab.RenderTransform = new TranslateTransform(draggingBounds.Width, 0);
                }
                else
                {
                    tab.RenderTransform = null;
                }
            }
        }
    }

    /// <summary>
    /// 回退模式下计算目标索引
    /// </summary>
    private int CalculateFallbackTargetIndex()
    {
        if (_draggingTab == null || _itemsPresenter?.Panel == null) return _draggingOriginalIndex;

        int newIndex = _draggingOriginalIndex;
        var panel = _itemsPresenter.Panel;

        foreach (var container in panel.Children)
        {
            if (container is BrowserTabItem tab && tab != _draggingTab && tab.RenderTransform is TranslateTransform t)
            {
                var index = IndexFromContainer(tab);
                if (t.X < 0) newIndex = Math.Max(newIndex, index);
                if (t.X > 0) newIndex = Math.Min(newIndex, index);
            }
        }

        return newIndex;
    }

    #endregion

    #region Drag - Tear Off

    /// <summary>
    /// 处理拖出标签栏区域的逻辑
    /// </summary>
    private void HandleDragOutside(PointerEventArgs e)
    {
        if (_draggingTab == null) return;

        var item = ItemFromContainer(_draggingTab) ?? _draggingTab;
        var args = new TabDraggedOutsideEventArgs(TabDraggedOutsideEvent, this, item, _draggingTab, e);

        // 清理拖拽状态
        _draggingTab.SetDraggingPseudoClass(false);
        _draggingTab.RenderTransform = null;
        e.Pointer.Capture(null);

        var panel = GetAnimatedPanel();
        panel?.ClearDragState();

        var savedTab = _draggingTab;
        _draggingTab = null;
        _isDragging = false;
        _draggingOriginalIndex = -1;
        ResetAllTabTransforms();

        // 触发事件，由外部处理创建新窗口的逻辑
        RaiseEvent(args);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 重置拖拽状态
    /// </summary>
    private void ResetDragState(PointerEventArgs? e)
    {
        if (_draggingTab != null && _isDragging)
        {
            _draggingTab.SetDraggingPseudoClass(false);
            _draggingTab.RenderTransform = null;
            if (e != null)
                e.Pointer.Capture(null);

            var panel = GetAnimatedPanel();
            panel?.ClearDragState();
        }

        _draggingTab = null;
        _isDragging = false;
        _draggingOriginalIndex = -1;
        ResetAllTabTransforms();
    }

    /// <summary>
    /// 清除所有标签的 RenderTransform
    /// </summary>
    private void ResetAllTabTransforms()
    {
        if (_itemsPresenter?.Panel == null) return;
        foreach (var container in _itemsPresenter.Panel.Children)
        {
            if (container is BrowserTabItem tab)
            {
                tab.RenderTransform = null;
            }
        }
    }

    /// <summary>
    /// 检查点击是否在按钮元素上（关闭按钮、左侧操作按钮）
    /// </summary>
    private static bool IsClickOnButton(PointerPressedEventArgs e, BrowserTabItem tabItem)
    {
        var source = e.Source as Control;
        while (source != null && source != tabItem)
        {
            if (source is Button) return true;
            source = source.Parent as Control;
        }
        return false;
    }

    #endregion
}

#region Event Args

public class TabCloseRequestedEventArgs : RoutedEventArgs
{
    public object Item { get; }
    public BrowserTabItem TabItem { get; }

    public TabCloseRequestedEventArgs(RoutedEvent routedEvent, object source, object item, BrowserTabItem tabItem)
        : base(routedEvent, source)
    {
        Item = item;
        TabItem = tabItem;
    }
}

public class TabDraggedOutsideEventArgs : RoutedEventArgs
{
    public object Item { get; }
    public BrowserTabItem TabItem { get; }
    public PointerEventArgs PointerEventArgs { get; }

    public TabDraggedOutsideEventArgs(RoutedEvent routedEvent, object source, object item, BrowserTabItem tabItem, PointerEventArgs pointerEventArgs)
        : base(routedEvent, source)
    {
        Item = item;
        TabItem = tabItem;
        PointerEventArgs = pointerEventArgs;
    }
}

#endregion
