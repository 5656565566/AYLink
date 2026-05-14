using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AYLink.Desktop.Behaviors;

public class FloatingDockBehavior : AvaloniaObject
{
    private class DragState
    {
        public bool IsPressed;
        public bool IsDragging;
        public Point StartPointerPosition;
        public Rect InitialBounds;
        public Thickness StartMargin;
        public bool IsSnappedToEdge;
        public bool IsDockedLeft;
        public int DragToken;
    }

    private static class GlobalDockState
    {
        public static bool IsInitialized = false;
        public static bool IsSnappedToEdge = true;
        public static bool IsDockedLeft = false;
        public static double TargetY = 16;
        public static double TargetX = 0;
    }

    private static readonly ConditionalWeakTable<Control, DragState> _states = [];
    private static readonly HashSet<Control> _activeControls = [];

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<FloatingDockBehavior, Control, bool>("IsEnabled", false);

    public static readonly AttachedProperty<bool> IsExpandedProperty =
        AvaloniaProperty.RegisterAttached<FloatingDockBehavior, Control, bool>("IsExpanded", false);

    public static bool GetIsEnabled(Control element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsExpanded(Control element) => element.GetValue(IsExpandedProperty);
    public static void SetIsExpanded(Control element, bool value) => element.SetValue(IsExpandedProperty, value);

    static FloatingDockBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(HandleIsEnabledChanged);
        IsExpandedProperty.Changed.AddClassHandler<Control>(HandleIsExpandedChanged);
        InputElement.IsPointerOverProperty.Changed.AddClassHandler<Control>(HandleIsPointerOverChanged);
    }

    private static void EnsureTransitions(Control control)
    {
        control.Transitions ??= [];
        
        bool hasMarginTransition = false;
        foreach (var t in control.Transitions)
        {
            if (t is ThicknessTransition tt && tt.Property == Avalonia.Layout.Layoutable.MarginProperty)
            {
                hasMarginTransition = true;
                break;
            }
        }
        
        if (!hasMarginTransition)
        {
            control.Transitions.Add(new ThicknessTransition 
            { 
                Property = Avalonia.Layout.Layoutable.MarginProperty, 
                Duration = TimeSpan.FromMilliseconds(250),
                Easing = new CubicEaseOut()
            });
        }
    }

    private static void EnableTransitions(Control control, bool enable)
    {
        if (control.Transitions == null) return;
        foreach (var t in control.Transitions)
        {
            if (t is ThicknessTransition mt && mt.Property == Avalonia.Layout.Layoutable.MarginProperty)
            {
                mt.Duration = enable ? TimeSpan.FromMilliseconds(250) : TimeSpan.Zero;
            }
        }
    }

    private static void HandleIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _activeControls.Add(control);
            var state = _states.GetOrCreateValue(control);
            
            if (GlobalDockState.IsInitialized)
            {
                state.IsSnappedToEdge = GlobalDockState.IsSnappedToEdge;
                state.IsDockedLeft = GlobalDockState.IsDockedLeft;
            }
            else
            {
                // 初始状态假定为靠右贴边
                state.IsSnappedToEdge = true;
                state.IsDockedLeft = false;
            }

            EnsureTransitions(control);

            if (GlobalDockState.IsInitialized)
            {
                ApplySolidState(control, state);
            }

            control.AddHandler(InputElement.PointerPressedEvent, Control_PointerPressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, Control_PointerMoved, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerReleasedEvent, Control_PointerReleased, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerCaptureLostEvent, Control_PointerCaptureLost, RoutingStrategies.Tunnel);
        }
        else
        {
            _activeControls.Remove(control);
            control.RemoveHandler(InputElement.PointerPressedEvent, Control_PointerPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, Control_PointerMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, Control_PointerReleased);
            control.RemoveHandler(InputElement.PointerCaptureLostEvent, Control_PointerCaptureLost);
            _states.Remove(control);
        }
    }

    private static void ApplySolidState(Control control, DragState state)
    {
        if (!GlobalDockState.IsInitialized) return;

        bool isExpanded = GetIsExpanded(control);
        double marginX = isExpanded ? 16 : (control.IsPointerOver ? 0 : -24);

        EnableTransitions(control, false);

        if (GlobalDockState.IsSnappedToEdge)
        {
            if (GlobalDockState.IsDockedLeft)
            {
                control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                control.Margin = new Thickness(marginX, GlobalDockState.TargetY, 0, 0);
            }
            else
            {
                control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                control.Margin = new Thickness(0, GlobalDockState.TargetY, marginX, 0);
            }
        }
        else
        {
            control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            control.Margin = new Thickness(GlobalDockState.TargetX, GlobalDockState.TargetY, 0, 0);
        }

        _ = ReenableTransitionsAsync(control);
    }

    private static async Task ReenableTransitionsAsync(Control control)
    {
        await Task.Delay(16);
        EnableTransitions(control, true);
    }

    private static void ApplyDockMargin(Control control, DragState state)
    {
        if (!state.IsSnappedToEdge) return;
        
        bool isExpanded = GetIsExpanded(control);
        double marginX = isExpanded ? 16 : (control.IsPointerOver ? 0 : -24);
        
        double targetY = GlobalDockState.IsInitialized ? GlobalDockState.TargetY : control.Margin.Top;

        EnableTransitions(control, true);

        if (state.IsDockedLeft)
        {
            if (control.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right)
            {
                control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            }
            control.Margin = new Thickness(marginX, targetY, 0, 0);
        }
        else
        {
            if (control.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right)
            {
                control.Margin = new Thickness(0, targetY, marginX, 0);
            }
            else if (control.Parent is Control parent)
            {
                double targetX = parent.Bounds.Width - marginX - control.Bounds.Width;
                control.Margin = new Thickness(targetX, targetY, 0, 0);
            }
        }
    }

    private static void HandleIsExpandedChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (!GetIsEnabled(control)) return;
        if (!_states.TryGetValue(control, out var state)) return;

        ApplyDockMargin(control, state);
    }

    private static void HandleIsPointerOverChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (!GetIsEnabled(control)) return;
        if (!_states.TryGetValue(control, out var state)) return;

        if (state.IsSnappedToEdge && !state.IsDragging)
        {
            ApplyDockMargin(control, state);
        }
    }

    private static void Control_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.Parent is not Control parent) return;

        // 修改：不要用 e.GetCurrentPoint(parent) 判断，直接判断 e.GetCurrentPoint(control) 
        // 或使用事件参数原始信息判断是不是左键
        var pointProperties = e.GetCurrentPoint(control).Properties;
        if (!pointProperties.IsLeftButtonPressed) return;

        var state = _states.GetOrCreateValue(control);
        state.DragToken++; // 中止之前的延迟操作

        state.IsPressed = true;
        state.IsDragging = false;
        state.StartPointerPosition = e.GetPosition(parent);
        state.InitialBounds = control.Bounds;
    }

    private static void Control_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control) return;
        if (!_states.TryGetValue(control, out var state) || !state.IsPressed) return;

        if (control.Parent is not Control parent) return;

        var currentPosition = e.GetPosition(parent);
        var deltaX = currentPosition.X - state.StartPointerPosition.X;
        var deltaY = currentPosition.Y - state.StartPointerPosition.Y;

        if (!state.IsDragging)
        {
            if (Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3)
            {
                state.IsDragging = true;
                
                // 拖拽时禁用过渡动画实现即时跟手
                EnableTransitions(control, false);

                double absoluteLeft = control.Bounds.X;
                double absoluteTop = control.Bounds.Y;

                control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                
                state.StartMargin = new Thickness(absoluteLeft, absoluteTop, 0, 0);
                control.Margin = state.StartMargin;
                
                e.Pointer.Capture(control);
            }
        }

        if (state.IsDragging)
        {
            control.Margin = new Thickness(
                state.StartMargin.Left + deltaX,
                state.StartMargin.Top + deltaY,
                0, 0);
            e.Handled = true;
        }
    }

    private static void Control_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control control) return;
        if (!_states.TryGetValue(control, out var state)) return;

        state.IsPressed = false;

        if (state.IsDragging)
        {
            e.Pointer.Capture(null);
            if (control.Parent is Control parent)
            {
                EndDrag(control, state, parent);
            }
            e.Handled = true;
        }
    }

    private static void Control_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is not Control control) return;
        if (!_states.TryGetValue(control, out var state)) return;

        state.IsPressed = false;
        if (state.IsDragging)
        {
            if (control.Parent is Control parent)
            {
                EndDrag(control, state, parent);
            }
        }
    }

    private static async void EndDrag(Control control, DragState state, Control parent)
    {
        state.IsDragging = false;
        int currentToken = ++state.DragToken;

        double absLeft = control.Margin.Left;
        double absTop = control.Margin.Top;
        double width = control.Bounds.Width;
        double height = control.Bounds.Height;

        double targetY = absTop;
        if (targetY < 16) targetY = 16;
        if (targetY + height > parent.Bounds.Height - 16) targetY = parent.Bounds.Height - 16 - height;

        // 仅当靠近左右边缘 60 像素时才进行吸附隐藏
        double snapThreshold = 60;
        bool isExpanded = GetIsExpanded(control);
        
        bool isSnapped = false;
        bool isDockedLeft = false;

        double targetX = absLeft;
        double marginX = isExpanded ? 16 : (control.IsPointerOver ? 0 : -24); 

        if (absLeft < snapThreshold)
        {
            isSnapped = true;
            isDockedLeft = true;
            targetX = marginX;
        }
        else if (absLeft + width > parent.Bounds.Width - snapThreshold)
        {
            isSnapped = true;
            isDockedLeft = false;
            targetX = parent.Bounds.Width - marginX - width;
        }
        else
        {
            // 自由悬浮 不吸附
            if (targetX < 16) targetX = 16;
            if (targetX + width > parent.Bounds.Width - 16) targetX = parent.Bounds.Width - 16 - width;
        }

        // 更新全局状态
        GlobalDockState.IsInitialized = true;
        GlobalDockState.IsSnappedToEdge = isSnapped;
        GlobalDockState.IsDockedLeft = isDockedLeft;
        GlobalDockState.TargetY = targetY;
        GlobalDockState.TargetX = targetX;

        // 立即同步到其他激活的控件
        foreach (var c in _activeControls)
        {
            if (c != control && _states.TryGetValue(c, out var s))
            {
                s.IsSnappedToEdge = GlobalDockState.IsSnappedToEdge;
                s.IsDockedLeft = GlobalDockState.IsDockedLeft;
                ApplySolidState(c, s);
            }
        }

        // 更新当前拖拽控件状态
        state.IsSnappedToEdge = isSnapped;
        state.IsDockedLeft = isDockedLeft;

        // 启用过渡动画并执行移动
        EnableTransitions(control, true);
        control.Margin = new Thickness(targetX, targetY, 0, 0);

        // 等待动画完成
        await Task.Delay(250);

        if (state.DragToken != currentToken) return;

        // 动画完成后固化状态（解决窗口 Resize 时的锚点对齐问题）
        if (state.IsSnappedToEdge)
        {
            ApplySolidState(control, state);
        }
    }
}
