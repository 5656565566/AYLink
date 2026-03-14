using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;

namespace AYLink.Controls;

[TemplatePart("PART_ItemsPresenter", typeof(ItemsPresenter))]
[TemplatePart("PART_AddButton", typeof(Button))]
public class BrowserTabView : TabControl
{
    public static readonly StyledProperty<bool> IsAddButtonVisibleProperty =
        AvaloniaProperty.Register<BrowserTabView, bool>(nameof(IsAddButtonVisible), true);

    public bool IsAddButtonVisible
    {
        get => GetValue(IsAddButtonVisibleProperty);
        set => SetValue(IsAddButtonVisibleProperty, value);
    }

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

    private Button? _addButton;
    private ItemsPresenter? _itemsPresenter;
    private BrowserTabItem? _draggingTab;
    private Point _dragStartPoint;
    private bool _isDragging;

    protected override Avalonia.Controls.Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new BrowserTabItem();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<BrowserTabItem>(item, out recycleKey);
    }

    protected override void PrepareContainerForItemOverride(Avalonia.Controls.Control container, object? item, int index)
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

    protected override void ClearContainerForItemOverride(Avalonia.Controls.Control container)
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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_addButton != null)
        {
            _addButton.Click -= AddButton_Click;
        }

        _addButton = e.NameScope.Find<Button>("PART_AddButton");
        _itemsPresenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");

        if (_addButton != null)
        {
            _addButton.Click += AddButton_Click;
        }
    }

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(AddTabRequestedEvent, this));
    }

    private void OnTabItemCloseRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is BrowserTabItem tabItem)
        {
            var item = ItemFromContainer(tabItem) ?? tabItem;
            RaiseEvent(new TabCloseRequestedEventArgs(TabCloseRequestedEvent, this, item, tabItem));
        }
    }

    private void OnTabItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is BrowserTabItem tabItem && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _draggingTab = tabItem;
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;
        }
    }

    private void OnTabItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingTab == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var currentPoint = e.GetPosition(this);
        var diff = currentPoint - _dragStartPoint;

        if (!_isDragging && (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3))
        {
            _isDragging = true;
            _draggingTab.SetDraggingPseudoClass(true);
            e.Pointer.Capture(_draggingTab);
        }

        if (_isDragging)
        {
            // Handle internal reordering
            UpdateTabPositions(currentPoint.X);

            // Check if dragged outside
            if (currentPoint.Y < -20 || currentPoint.Y > Bounds.Height + 20)
            {
                HandleDragOutside(e);
            }
        }
    }

    private void OnTabItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingTab != null)
        {
            if (_isDragging)
            {
                _draggingTab.SetDraggingPseudoClass(false);
                _draggingTab.RenderTransform = null;
                e.Pointer.Capture(null);
                CommitReorder();
            }
            _draggingTab = null;
            _isDragging = false;
            ResetTabPositions();
        }
    }

    private void UpdateTabPositions(double currentX)
    {
        if (_draggingTab == null || _itemsPresenter?.Panel == null) return;

        var panel = _itemsPresenter.Panel;
        var draggingIndex = IndexFromContainer(_draggingTab);
        var draggingBounds = _draggingTab.Bounds;

        _draggingTab.RenderTransform = new TranslateTransform(currentX - _dragStartPoint.X, 0);

        int newIndex = draggingIndex;

        foreach (var container in panel.Children)
        {
            if (container is BrowserTabItem tab && tab != _draggingTab)
            {
                var index = IndexFromContainer(tab);
                var bounds = tab.Bounds;
                var center = bounds.X + bounds.Width / 2;

                if (draggingIndex < index && currentX > center)
                {
                    tab.RenderTransform = new TranslateTransform(-draggingBounds.Width, 0);
                    newIndex = Math.Max(newIndex, index);
                }
                else if (draggingIndex > index && currentX < center)
                {
                    tab.RenderTransform = new TranslateTransform(draggingBounds.Width, 0);
                    newIndex = Math.Min(newIndex, index);
                }
                else
                {
                    tab.RenderTransform = null;
                }
            }
        }
    }

    private void CommitReorder()
    {
        if (_draggingTab == null || _itemsPresenter?.Panel == null) return;

        var panel = _itemsPresenter.Panel;
        var draggingIndex = IndexFromContainer(_draggingTab);
        int newIndex = draggingIndex;

        foreach (var container in panel.Children)
        {
            if (container is BrowserTabItem tab && tab != _draggingTab && tab.RenderTransform is TranslateTransform t)
            {
                var index = IndexFromContainer(tab);
                if (t.X < 0) newIndex = Math.Max(newIndex, index);
                if (t.X > 0) newIndex = Math.Min(newIndex, index);
            }
        }

        if (newIndex != draggingIndex && Items is IList list)
        {
            var item = ItemFromContainer(_draggingTab) ?? _draggingTab;
            list.RemoveAt(draggingIndex);
            list.Insert(newIndex, item);
            SelectedIndex = newIndex;
        }
    }

    private void ResetTabPositions()
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

    private void HandleDragOutside(PointerEventArgs e)
    {
        if (_draggingTab == null) return;

        var item = ItemFromContainer(_draggingTab) ?? _draggingTab;
        var args = new TabDraggedOutsideEventArgs(TabDraggedOutsideEvent, this, item, _draggingTab, e);
        
        _draggingTab.SetDraggingPseudoClass(false);
        _draggingTab.RenderTransform = null;
        e.Pointer.Capture(null);
        _draggingTab = null;
        _isDragging = false;
        ResetTabPositions();

        RaiseEvent(args);
    }
}

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
