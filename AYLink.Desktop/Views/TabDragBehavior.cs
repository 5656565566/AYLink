using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AYLink.Desktop.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;

namespace AYLink.Desktop.Views;

internal sealed record TabDragHost(object Owner, IList Tabs, string? PageKey, IDataTemplate? Template, FATabView View,
    Func<TabItemViewModelBase, int, bool> Move,
    Func<TabItemViewModelBase, bool> Detach,
    Func<TabItemViewModelBase, int, IDataTemplate?, bool> Attach,
    Action CompleteTransfer);

/// <summary>
/// 栏内实时绘制表头，离栏后拖动真实窗口；进入同类标签栏立即归并，取消则恢复原宿主。
/// </summary>
internal sealed class TabDragBehavior
{
    private static readonly List<TabDragBehavior> Hosts = [];
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(150);
    private static TabDragBehavior? _active;
    private static long _activationSerial;

    private readonly Control _owner;
    private readonly FATabView _tabs;
    private readonly Func<TabDragHost?> _getHost;
    private readonly Func<FATabView> _getVisibleTabs;
    private readonly Dictionary<FATabViewItem, HeaderMotion> _motions = [];
    private Window? _window;
    private long _activationOrder;
    private TabDragHost? _source;
    private TabItemViewModelBase? _tab;
    private FATabViewItem? _pressedHeader;
    private IPointer? _pointer;
    private Point _press;
    private Point _grab;
    private Size _headerSize;
    private PixelPoint _screenPoint;
    private int _sourceIndex;
    private int _insertIndex;
    private bool _dragging;
    private TabDragBehavior? _current;
    private TabDragHost? _currentHost;
    private TabDragBehavior? _inputHost;
    private TabDragBehavior? _rejectedTarget;
    private DetachedTabWindow? _floatingWindow;
    private TabDragHost? _floatingHost;
    private FATabViewItem? _floatingHeader;
    private bool _floatingWasTopmost;
    private PixelPoint _originalWindowPosition;
    private WindowState _originalWindowState;
    private bool _changingHosts;
    private bool _positioningWindow;
    private Canvas? _overlay;
    private FATabViewItem? _dragHeader;
    private IDisposable? _settle;
    private TimeSpan? _lastFrame;
    private int _frameVersion;

    public TabDragBehavior(Control owner, FATabView tabs, Func<TabDragHost?> getHost, Func<FATabView>? getVisibleTabs = null)
    {
        _owner = owner;
        _tabs = tabs;
        _getHost = getHost;
        _getVisibleTabs = getVisibleTabs ?? (() => tabs);
        owner.AttachedToVisualTree += OnAttached;
        owner.DetachedFromVisualTree += OnDetached;
        owner.DataContextChanged += OnHostChanged;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        owner.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        Register();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Register();

    private void Register()
    {
        if (_window != null || TopLevel.GetTopLevel(_owner) is not Window window) return;
        _window = window;
        _activationOrder = ++_activationSerial;
        Hosts.Add(this);
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.Closing += OnWindowClosing;
        window.Closed += OnClosed;
        window.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct, true);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Unregister();
    private void OnClosed(object? sender, EventArgs e) => Unregister();
    private void OnActivated(object? sender, EventArgs e) => _activationOrder = ++_activationSerial;
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_active is { } active && active._inputHost == this && !active._changingHosts) active.Cancel();
    }
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_active is { } active && (active == this || active._current == this)) active.Cancel();
    }

    private void OnHostChanged(object? sender, EventArgs e)
    {
        if (_active?._changingHosts == true) return;
        if (_active is { } active && (active == this || active._current == this)) active.Cancel();
        ResetVisuals();
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty) OnHostChanged(sender, EventArgs.Empty);
    }

    private void Unregister()
    {
        OnHostChanged(this, EventArgs.Empty);
        Hosts.Remove(this);
        if (_window == null) return;
        _window.Activated -= OnActivated;
        _window.Deactivated -= OnDeactivated;
        _window.Closing -= OnWindowClosing;
        _window.Closed -= OnClosed;
        _window.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _window.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _window.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _window = null;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null || e.Pointer.Type != PointerType.Mouse || !e.GetCurrentPoint(_owner).Properties.IsLeftButtonPressed)
            return;
        var header = (e.Source as Visual)?.FindAncestorOfType<FATabViewItem>(true);
        if (header == null || header.FindAncestorOfType<FATabView>() != _tabs) return;
        for (var visual = e.Source as Visual; visual != null && visual != header; visual = visual.GetVisualParent())
        {
            if (visual is Button) return;
        }
        var tab = GetTab(header);
        var host = _getHost();
        if (tab is not { IsClosable: true } || host == null || !host.Tabs.Contains(tab)) return;

        _active?.Cancel();
        StopSettling();
        _active = this;
        _source = host;
        _current = this;
        _currentHost = host;
        _inputHost = this;
        _tab = tab;
        _sourceIndex = host.Tabs.IndexOf(tab);
        _originalWindowPosition = _window.Position;
        _originalWindowState = _window.WindowState;
        _pressedHeader = header;
        _press = e.GetPosition(_window);
        _grab = e.GetPosition(header);
        _pointer = e.Pointer;
        if (host.Tabs is INotifyCollectionChanged collection) collection.CollectionChanged += OnCollectionChanged;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_active is { } active && active._inputHost == this && e.Pointer == active._pointer) active.Move(e);
    }

    private void Move(PointerEventArgs e)
    {
        var inputWindow = _inputHost!._window!;
        if (!e.GetCurrentPoint(inputWindow).Properties.IsLeftButtonPressed || !IsSourceCurrent()) { Cancel(); return; }
        var position = e.GetPosition(inputWindow);
        if (!_dragging)
        {
            var delta = position - _press;
            if (delta.X * delta.X + delta.Y * delta.Y < 25) return;
            _headerSize = _pressedHeader!.Bounds.Size;
            _dragging = true;
            // 捕获到窗口，允许指针离开标签栏甚至离开客户区；点击阶段仍交给 TabView 处理。
            e.Pointer.Capture(inputWindow);
            RequestFrames();
        }
        _screenPoint = inputWindow.PointToScreen(position);
        UpdatePreview();
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_active is { } active && active._inputHost == this && e.Pointer == active._pointer) active.Release(e);
    }

    private void Release(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (!_dragging) { EndTracking(); return; }
        var inputWindow = _inputHost!._window!;
        _screenPoint = inputWindow.PointToScreen(e.GetPosition(inputWindow));
        UpdatePreview();
        if (_active != this) return;
        if (_rejectedTarget != null) { Cancel(); e.Handled = true; return; }

        var source = _source!;
        var tab = _tab!;
        var current = _current;
        var host = _currentHost!;
        var index = _insertIndex;
        var floating = _floatingWindow;
        var floatingHost = _floatingHost;
        var before = current?.CapturePositions();
        EndTracking();
        e.Handled = true;

        if (current != null)
        {
            var selected = current._tabs.SelectedItem;
            host.Move(tab, index);
            // FluentAvalonia 会在 Move 通知中清空内部选择，VM 的同值绑定不会自动回写。
            current._tabs.SetCurrentValue(FATabView.SelectedItemProperty, selected);
            current._window!.Activate();
        }
        else
        {
            floating!.Activate();
        }
        source.CompleteTransfer();
        if (floatingHost?.Owner != source.Owner) floatingHost?.CompleteTransfer();
        if (before != null) current!.AnimatePositions(before);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_active is { } active && active._inputHost == this && e.Key == Key.Escape) { active.Cancel(); e.Handled = true; }
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_active is { } active && active._inputHost == this && active._dragging && !active._changingHosts &&
            e.Pointer == active._pointer && e.Pointer.Captured != active._inputHost._window) active.Cancel();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_changingHosts) Cancel();
    }

    private bool IsSourceCurrent()
    {
        var host = _getHost();
        return _source != null && host?.Owner == _source.Owner && host.Tabs == _source.Tabs &&
            host.PageKey == _source.PageKey && _currentHost?.Tabs.Contains(_tab) == true;
    }

    private void UpdatePreview()
    {
        if (!IsSourceCurrent()) { Cancel(); return; }
        if (_current == null)
        {
            PositionFloatingWindow();
            TryDock();
            return;
        }
        var current = _current;
        if (!current._owner.IsEffectivelyVisible || !current.TryGetBar(out var bar, out var bounds)) { Cancel(); return; }
        if (current._tabs.ContainerFromItem(_tab) is FATabViewItem { Bounds.Width: > 0, Bounds.Height: > 0 } header)
            _headerSize = header.Bounds.Size;
        var point = current._window!.PointToClient(_screenPoint);
        // 单标签独立窗口直接随表头移动；多标签窗口离栏留半个栏高的容差。
        if (!(current._window is DetachedTabWindow && _currentHost!.Tabs.Count == 1) &&
            bounds.Inflate(new Thickness(0, bounds.Height / 2)).Contains(point))
        {
            _insertIndex = current.GetInsertIndex(point.X - _grab.X + _headerSize.Width / 2, _currentHost!, _tab);
            current.EnsureOverlay(bar);
            if (current._dragHeader == null)
            {
                current._dragHeader = new DragHeader
                {
                    DataContext = _tab, IsSelected = true, IsHitTestVisible = false
                };
                current._dragHeader.Bind(FATabViewItem.HeaderProperty, new Binding(nameof(TabItemViewModelBase.Title)));
                current._dragHeader.Bind(FATabViewItem.IsClosableProperty, new Binding(nameof(TabItemViewModelBase.IsClosable)));
                current._overlay!.Children.Add(current._dragHeader);
            }
            current._dragHeader.Width = _headerSize.Width;
            current._dragHeader.Height = _headerSize.Height;
            var viewport = current.GetViewport() ?? bounds;
            var scroller = current.GetList()?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller != null && scroller.Extent.Width <= scroller.Viewport.Width)
                viewport = new Rect(viewport.Left, bounds.Top, bounds.Right - viewport.Left, bounds.Height);
            var left = Math.Clamp(point.X - _grab.X, viewport.Left, Math.Max(viewport.Left, viewport.Right - _headerSize.Width));
            var origin = bar.TranslatePoint(default, current._window!)!.Value;
            var scaling = current._window!.RenderScaling;
            Canvas.SetLeft(current._dragHeader, Math.Round((left - origin.X) * scaling) / scaling);
            Canvas.SetTop(current._dragHeader, Math.Round((bounds.Bottom - origin.Y - _headerSize.Height) * scaling) / scaling);
            current._overlay!.Clip = new RectangleGeometry(new Rect(viewport.X - origin.X, 0, viewport.Width, bar.Bounds.Height));
            current.PreviewReorder(_currentHost!, _tab!, _insertIndex, _headerSize.Width);
        }
        else
        {
            if (!StartFloatingWindow()) { Cancel(); return; }
            TryDock();
        }
    }

    private void TryDock()
    {
        var target = FindTarget(_screenPoint);
        if (target == _rejectedTarget) return;
        _rejectedTarget = null;
        if (target == null || target._getHost() is not { } host) return;
        var point = target._window!.PointToClient(_screenPoint);
        var index = target.GetInsertIndex(point.X, host, null);
        var before = target.CapturePositions();
        _changingHosts = true;
        try
        {
            if (!Transfer(_floatingHost!, host, _tab!, _floatingHost!.Tabs.IndexOf(_tab), index))
            {
                _rejectedTarget = target;
                FindFloatingHeader();
                return;
            }
            SetCurrent(target, host);
            target._window!.UpdateLayout();
            // 虚拟化列表不会为屏外标签创建表头，先按插入索引将其滚入可见范围。
            target.GetList()?.ScrollIntoView(index);
            SetInputHost(target);
            // 先把捕获交给目标，再隐藏空窗口；保持本次按住手势连续，Esc 仍可恢复来源。
            _floatingWindow!.Hide();
            _floatingHeader = null;
            target.AnimatePositions(before, settle: false);
        }
        finally { _changingHosts = false; }
        UpdatePreview();
    }

    private void PreviewReorder(TabDragHost host, TabItemViewModelBase moving, int insertIndex, double width)
    {
        var sourceIndex = host.Tabs.IndexOf(moving);
        foreach (var (header, motion) in GetMotions())
        {
            var tab = GetTab(header);
            var index = host.Tabs.IndexOf(tab);
            motion.SetHidden(tab == moving);
            var offset = index > sourceIndex && index <= insertIndex ? -width :
                index < sourceIndex && index >= insertIndex ? width : 0;
            motion.MoveTo(offset);
        }
    }

    private int GetInsertIndex(double x, TabDragHost host, TabItemViewModelBase? moving)
    {
        var original = moving == null ? -1 : host.Tabs.IndexOf(moving);
        var index = original < 0 ? host.Tabs.Count : original;
        foreach (var header in GetHeaders())
        {
            var tab = GetTab(header);
            if (tab == moving) continue;
            var itemIndex = host.Tabs.IndexOf(tab);
            if (itemIndex < 0) continue;
            var rect = GetHeaderBounds(header);
            var candidate = itemIndex - (original >= 0 && itemIndex > original ? 1 : 0);
            if (x < rect.Center.X) { index = candidate; break; }
            index = candidate + 1;
        }
        if (moving == null)
        {
            // 不把外来标签插到固定入口之前。
            while (index < host.Tabs.Count && host.Tabs[index] is TabItemViewModelBase { IsClosable: false }) index++;
            return index;
        }
        var first = original;
        var last = original;
        while (first > 0 && host.Tabs[first - 1] is TabItemViewModelBase { IsClosable: true }) first--;
        while (last + 1 < host.Tabs.Count && host.Tabs[last + 1] is TabItemViewModelBase { IsClosable: true }) last++;
        return Math.Clamp(index, first, last);
    }

    private void SetCurrent(TabDragBehavior? current, TabDragHost host)
    {
        if (_currentHost?.Tabs is INotifyCollectionChanged oldCollection &&
            _currentHost.Tabs != _source?.Tabs && _currentHost.Tabs != _floatingHost?.Tabs)
            oldCollection.CollectionChanged -= OnCollectionChanged;
        _current?.ResetVisuals();
        _current = current;
        _currentHost = host;
        current?.StopSettling();
        if (host.Tabs is INotifyCollectionChanged collection && host.Tabs != _source?.Tabs && host.Tabs != _floatingHost?.Tabs)
            collection.CollectionChanged += OnCollectionChanged;
    }

    private TabDragBehavior? FindTarget(PixelPoint point)
    {
        // 上层窗口的内容区域也会挡住其后的标签栏，不能穿透合并。
        foreach (var group in Hosts.Where(h => h._window != _floatingWindow && h._window is { IsVisible: true, WindowState: not WindowState.Minimized })
                     .GroupBy(h => h._window!).OrderByDescending(g => g.Key.Topmost).ThenByDescending(g => g.Max(h => h._activationOrder)))
        {
            var window = group.Key;
            if (!new Rect(window.Bounds.Size).Contains(window.PointToClient(point))) continue;
            return group.FirstOrDefault(h => h._owner.IsEffectivelyVisible &&
                _source?.PageKey != null && h._getHost()?.PageKey == _source.PageKey &&
                h.TryGetBar(out _, out var bounds) && bounds.Contains(window.PointToClient(point)));
        }
        return null;
    }

    private bool TryGetBar(out Grid bar, out Rect bounds)
    {
        var view = _getVisibleTabs();
        bar = view.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == "TabContainerGrid")!;
        bounds = default;
        if (bar == null || _window == null || !view.IsEffectivelyVisible || bar.Bounds.Height <= 0) return false;
        var origin = bar.TranslatePoint(default, _window);
        if (origin == null) return false;
        var captionSpace = bar.GetVisualChildren().OfType<ContentPresenter>().First(p => p.Name == "RightContentPresenter").MinWidth;
        bounds = new Rect(origin.Value, new Size(Math.Max(0, bar.Bounds.Width - captionSpace), bar.Bounds.Height));
        return bounds.Width > 0;
    }

    private FATabViewListView? GetList() => _getVisibleTabs().GetVisualDescendants().OfType<FATabViewListView>().FirstOrDefault();

    private FATabViewItem[] GetHeaders()
    {
        var list = GetList();
        return list?.GetRealizedContainers().OfType<FATabViewItem>().OrderBy(list.IndexFromContainer).ToArray() ?? [];
    }

    private Rect? GetViewport()
    {
        var viewport = GetList()?.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        var origin = viewport?.TranslatePoint(default, _window!);
        return origin.HasValue ? new Rect(origin.Value, viewport!.Bounds.Size) : null;
    }

    private Rect GetHeaderBounds(FATabViewItem header)
    {
        var origin = header.TranslatePoint(default, _window!)!.Value;
        if (_motions.TryGetValue(header, out var motion)) origin = origin.WithX(origin.X - motion.Offset);
        return new Rect(origin, header.Bounds.Size);
    }

    private IEnumerable<KeyValuePair<FATabViewItem, HeaderMotion>> GetMotions()
    {
        var headers = GetHeaders();
        foreach (var (header, motion) in _motions.ToArray())
        {
            if (!headers.Contains(header) || motion.Tab != GetTab(header)) { motion.Dispose(); _motions.Remove(header); }
        }
        foreach (var header in headers)
        {
            if (!_motions.ContainsKey(header)) _motions.Add(header, new HeaderMotion(header));
        }
        return _motions;
    }

    private void EnsureOverlay(Grid bar)
    {
        if (_overlay?.Parent == bar) return;
        RemoveOverlay();
        _overlay = new Canvas { IsHitTestVisible = false, ZIndex = 1000 };
        Grid.SetColumnSpan(_overlay, bar.ColumnDefinitions.Count);
        bar.Children.Add(_overlay);
    }

    private bool StartFloatingWindow()
    {
        var current = _current!;
        var host = _currentHost!;
        current.ResetVisuals();
        if (_floatingWindow == null)
        {
            var reuseWindow = current._window is DetachedTabWindow && host.Tabs.Count == 1;
            _floatingWindow = reuseWindow ? (DetachedTabWindow)current._window! : new DetachedTabWindow
            {
                SourcePageKey = host.PageKey, DefaultContentTemplate = host.Template,
                Width = current._owner.Bounds.Width, Height = current._owner.Bounds.Height,
                Position = _screenPoint
            };
            _floatingHost = reuseWindow ? host : _floatingWindow.GetDragHost();
            _floatingWasTopmost = _floatingWindow.Topmost;
            if (_floatingHost.Tabs != _source!.Tabs && _floatingHost.Tabs is INotifyCollectionChanged collection)
                collection.CollectionChanged += OnCollectionChanged;
            _floatingWindow.Closing += OnFloatingClosing;
            _floatingWindow.ScalingChanged += OnFloatingScalingChanged;
        }
        var floating = _floatingWindow;
        _changingHosts = true;
        try
        {
            floating.Topmost = true;
            floating.WindowState = WindowState.Normal;
            if (host.Owner == _floatingHost!.Owner)
            {
                PrepareFloatingWindow();
            }
            else
            {
                if (!Transfer(host, _floatingHost, _tab!, host.Tabs.IndexOf(_tab), 0, PrepareFloatingWindow)) return false;
            }
            SetCurrent(null, _floatingHost);
            SetInputHost(Hosts.Single(h => h._owner == floating));
            return true;
        }
        finally { _changingHosts = false; }
    }

    private void PrepareFloatingWindow()
    {
        var floating = _floatingWindow!;
        if (!floating.IsVisible) floating.Show();
        DetachedTabWindowManager.Instance.Register(floating);
        FindFloatingHeader();
    }

    private void FindFloatingHeader()
    {
        var floating = _floatingWindow!;
        floating.UpdateLayout();
        _floatingHeader = floating.GetVisualDescendants().OfType<FATabViewItem>().Single(h => h.Content == _tab);
        PositionFloatingWindow();
    }

    private void OnFloatingClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_active == this) Cancel(closeFloating: false);
    }

    private void OnFloatingScalingChanged(object? sender, EventArgs e)
    {
        if (_current != null) return;
        _floatingWindow?.UpdateLayout();
        PositionFloatingWindow();
    }

    private void PositionFloatingWindow()
    {
        if (_floatingWindow == null || _floatingHeader == null || _positioningWindow) return;
        _positioningWindow = true;
        try
        {
            // 使用当前窗口的实际表头和缩放换算抓取点，跨屏后仍由每帧跟随校准。
            var anchor = _floatingHeader.PointToScreen(_grab);
            var position = _floatingWindow.Position;
            _floatingWindow.Position = new PixelPoint(position.X + _screenPoint.X - anchor.X,
                position.Y + _screenPoint.Y - anchor.Y);
        }
        finally { _positioningWindow = false; }
    }

    private void ReleaseFloatingWindow()
    {
        if (_floatingWindow == null) return;
        if (_floatingHost?.Tabs != _source?.Tabs && _floatingHost?.Tabs is INotifyCollectionChanged collection)
            collection.CollectionChanged -= OnCollectionChanged;
        _floatingWindow.Closing -= OnFloatingClosing;
        _floatingWindow.ScalingChanged -= OnFloatingScalingChanged;
        _floatingWindow.Topmost = _floatingWasTopmost;
        _floatingWindow = null;
        _floatingHost = null;
        _floatingHeader = null;
    }

    private void SetInputHost(TabDragBehavior host)
    {
        _inputHost = host;
        host._window!.Activate();
        _pointer!.Capture(host._window);
        RequestFrames();
    }

    private void RequestFrames()
    {
        _lastFrame = null;
        var version = ++_frameVersion;
        _inputHost!._window!.RequestAnimationFrame(time => OnFrame(time, version));
    }

    private void OnFrame(TimeSpan time, int version)
    {
        if (_active != this || !_dragging || version != _frameVersion) return;
        if (!IsSourceCurrent() || _current is { } current && !current._owner.IsEffectivelyVisible) { Cancel(); return; }
        if (_current == null) PositionFloatingWindow();
        var seconds = _lastFrame.HasValue ? Math.Clamp((time - _lastFrame.Value).TotalSeconds, 0, 0.05) : 0;
        _lastFrame = time;
        var host = _current;
        var scroller = host?.GetList()?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller != null && host!.GetViewport() is { } viewport)
        {
            var x = host._window!.PointToClient(_screenPoint).X;
            const double edge = 24;
            var speed = x < viewport.Left + edge ? -480 : x > viewport.Right - edge ? 480 : 0;
            if (speed != 0 && scroller.Extent.Width > scroller.Viewport.Width)
            {
                scroller.Offset = scroller.Offset.WithX(Math.Clamp(scroller.Offset.X + speed * seconds, 0,
                    scroller.Extent.Width - scroller.Viewport.Width));
                host._window!.UpdateLayout();
                UpdatePreview();
            }
        }
        if (_active == this && version == _frameVersion) _inputHost?._window?.RequestAnimationFrame(next => OnFrame(next, version));
    }

    private Dictionary<TabItemViewModelBase, double> CapturePositions()
    {
        var positions = new Dictionary<TabItemViewModelBase, double>();
        if (_window == null) return positions;
        foreach (var header in GetHeaders())
        {
            if (GetTab(header) is { } tab) positions[tab] = header.TranslatePoint(default, _window)!.Value.X;
        }
        if (_active?._tab is { } moving && _dragHeader is { IsVisible: true })
            positions[moving] = _dragHeader.TranslatePoint(default, _window)!.Value.X;
        return positions;
    }

    private void AnimatePositions(Dictionary<TabItemViewModelBase, double> before, bool settle = true)
    {
        if (_window is not { IsVisible: true }) return;
        _window.UpdateLayout();
        foreach (var (header, motion) in GetMotions())
        {
            if (motion.Tab != null && before.TryGetValue(motion.Tab, out var x))
                motion.MoveTo(x - GetHeaderBounds(header).X, false);
            motion.MoveTo(0);
        }
        if (settle) ScheduleVisualReset();
    }

    private void Cancel(bool closeFloating = true)
    {
        if (_active != this) return;
        var current = _current;
        var before = current?.CapturePositions();
        var source = _source;
        var currentHost = _currentHost;
        var tab = _tab;
        var index = _sourceIndex;
        var floating = _floatingWindow;
        var floatingHost = _floatingHost;
        var position = _originalWindowPosition;
        var state = _originalWindowState;
        EndTracking();
        if (source != null && tab != null && currentHost?.Tabs.Contains(tab) == true)
        {
            var restored = currentHost.Owner == source.Owner ||
                Transfer(currentHost, source, tab, currentHost.Tabs.IndexOf(tab), Math.Min(index, source.Tabs.Count));
            if (restored)
            {
                source.Move(tab, Math.Min(index, source.Tabs.Count - 1));
                source.View.SetCurrentValue(FATabView.SelectedItemProperty, tab);
                if (_window is DetachedTabWindow && !_window.IsVisible) _window.Show();
                _window!.WindowState = state;
                _window.Position = position;
                _window.Activate();
            }
            else (current?._window ?? floating)?.Activate();
        }
        if (closeFloating && floatingHost?.Owner != source?.Owner) floatingHost?.CompleteTransfer();
        if (before != null) current!.AnimatePositions(before);
    }

    private void EndTracking()
    {
        if (_source?.Tabs is INotifyCollectionChanged collection) collection.CollectionChanged -= OnCollectionChanged;
        _active = null;
        if (_currentHost?.Tabs is INotifyCollectionChanged currentCollection &&
            _currentHost.Tabs != _source?.Tabs && _currentHost.Tabs != _floatingHost?.Tabs)
            currentCollection.CollectionChanged -= OnCollectionChanged;
        _current?.ResetVisuals();
        ReleaseFloatingWindow();
        _source = null;
        _tab = null;
        _pressedHeader = null;
        _dragging = false;
        _lastFrame = null;
        _frameVersion++;
        if (_pointer is { } pointer && pointer.Captured == _inputHost?._window) pointer.Capture(null);
        _pointer = null;
        _current = null;
        _currentHost = null;
        _inputHost = null;
        _rejectedTarget = null;
        ResetVisuals();
    }

    private void ResetVisuals(bool animate = false)
    {
        _settle?.Dispose();
        _settle = null;
        RemoveOverlay();
        foreach (var motion in _motions.Values)
        {
            motion.SetHidden(false);
            if (animate) motion.MoveTo(0);
            else motion.Dispose();
        }
        if (animate) ScheduleVisualReset();
        else _motions.Clear();
    }

    private void ScheduleVisualReset()
    {
        _settle?.Dispose();
        _settle = DispatcherTimer.RunOnce(() => ResetVisuals(), SlideDuration);
    }

    private void StopSettling()
    {
        _settle?.Dispose();
        _settle = null;
        foreach (var motion in _motions.Values) motion.MoveTo(motion.Offset, false);
    }

    private void RemoveOverlay()
    {
        if (_overlay?.Parent is Panel panel) panel.Children.Remove(_overlay);
        _overlay = null;
        _dragHeader = null;
    }

    private static bool Transfer(TabDragHost source, TabDragHost target, TabItemViewModelBase tab,
        int oldIndex, int index, Action? showTarget = null)
    {
        try
        {
            if (!DetachForTransfer(source, tab)) return false;
            if (!target.Attach(tab, index, source.Template)) throw new InvalidOperationException("Target rejected tab transfer.");
            showTarget?.Invoke();
        }
        catch (Exception ex)
        {
            if (target.Tabs.Contains(tab)) DetachForTransfer(target, tab);
            if (!source.Tabs.Contains(tab) && !source.Attach(tab, oldIndex, source.Template))
                throw new InvalidOperationException("Could not restore tab to its original host.", ex);
            Debug.WriteLine($"[TabDrag] Transfer rolled back: {ex}");
            Services.Notifications.NotificationService.Instance.ShowError(
                Services.Localization.LocalizationManager.Instance.GetString("Dialog.Error", "错误"),
                Services.Localization.LocalizationManager.Instance.GetString("TabbedPageView.TransferFailed", "标签页转移失败，已保留在原窗口"));
            return false;
        }
        return true;
    }

    private static bool DetachForTransfer(TabDragHost host, TabItemViewModelBase tab)
    {
        if (!host.Detach(tab)) return false;
        // TabView 延迟刷新内容；先卸载旧视图，避免其解绑回调在新视图挂载后清掉投屏引用。
        var content = host.View.GetVisualDescendants().OfType<ContentPresenter>()
            .FirstOrDefault(p => p.Name == "TabContentPresenter" && ReferenceEquals(p.Content, tab));
        if (content != null) content.Content = null;
        return true;
    }

    private static TabItemViewModelBase? GetTab(FATabViewItem header) =>
        header.Content as TabItemViewModelBase ?? header.DataContext as TabItemViewModelBase;

    private sealed class DragHeader : FATabViewItem
    {
        protected override Type StyleKeyOverride => typeof(FATabViewItem);

        public DragHeader()
        {
            // 覆盖层不在原生列表中，显式声明顶部状态以复用现有标签主题。
            PseudoClasses.Set(":top", true);
        }

        protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            if (TabSeparator != null) TabSeparator.Opacity = 0;
        }
    }

    private sealed class HeaderMotion : IDisposable
    {
        private readonly FATabViewItem _header;
        private readonly TranslateTransform _translation = new();
        private readonly Transitions _transitions = [new DoubleTransition
        {
            Property = TranslateTransform.XProperty, Duration = SlideDuration, Easing = new CubicEaseOut()
        }];
        private readonly IDisposable? _transform;
        private IDisposable? _hidden;
        public TabItemViewModelBase? Tab { get; }
        public double Offset => _translation.X;

        public HeaderMotion(FATabViewItem header)
        {
            _header = header;
            Tab = GetTab(header);
            _transform = header.SetValue(Visual.RenderTransformProperty, _translation, BindingPriority.Animation);
        }

        public void MoveTo(double x, bool animate = true)
        {
            _translation.Transitions = animate ? _transitions : null;
            _translation.X = x;
        }

        public void SetHidden(bool hidden)
        {
            if (hidden) _hidden ??= _header.SetValue(Visual.OpacityProperty, 0d, BindingPriority.Animation);
            else { _hidden?.Dispose(); _hidden = null; }
        }

        public void Dispose() { _hidden?.Dispose(); _transform?.Dispose(); }
    }
}
