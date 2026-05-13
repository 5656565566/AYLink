using System.Runtime.CompilerServices;

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

    private static readonly ConditionalWeakTable<Control, DragState> _states = [];

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
    }

    private static void EnsureTransitions(Control control)
    {
        if (control.Transitions == null)
        {
            control.Transitions = [];
        }
        
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
            var state = _states.GetOrCreateValue(control);
            // 初始状态假定为靠右贴边
            state.IsSnappedToEdge = true;
            state.IsDockedLeft = false;

            EnsureTransitions(control);

            control.AddHandler(InputElement.PointerPressedEvent, Control_PointerPressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, Control_PointerMoved, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerReleasedEvent, Control_PointerReleased, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerCaptureLostEvent, Control_PointerCaptureLost, RoutingStrategies.Tunnel);
        }
        else
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, Control_PointerPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, Control_PointerMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, Control_PointerReleased);
            control.RemoveHandler(InputElement.PointerCaptureLostEvent, Control_PointerCaptureLost);
            _states.Remove(control);
        }
    }

    private static void HandleIsExpandedChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (!GetIsEnabled(control)) return;
        if (!_states.TryGetValue(control, out var state)) return;

        bool isExpanded = e.NewValue is true;
        
        if (state.IsSnappedToEdge)
        {
            // 收起时隐藏一半（球的宽度为40）展开时距边缘16
            double marginX = isExpanded ? 16 : -24;
            var currentMargin = control.Margin;

            EnableTransitions(control, true);

            if (state.IsDockedLeft)
            {
                control.Margin = new Thickness(marginX, currentMargin.Top, 0, 0);
            }
            else
            {
                control.Margin = new Thickness(0, currentMargin.Top, marginX, 0);
            }
        }
    }

    private static void Control_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.Parent is not Control parent) return;

        if (!e.GetCurrentPoint(parent).Properties.IsLeftButtonPressed) return;

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
        
        state.IsSnappedToEdge = false;

        double targetX = absLeft;
        double marginX = isExpanded ? 16 : -24; 

        if (absLeft < snapThreshold)
        {
            state.IsSnappedToEdge = true;
            state.IsDockedLeft = true;
            targetX = marginX;
        }
        else if (absLeft + width > parent.Bounds.Width - snapThreshold)
        {
            state.IsSnappedToEdge = true;
            state.IsDockedLeft = false;
            targetX = parent.Bounds.Width - marginX - width;
        }
        else
        {
            // 自由悬浮 不吸附
            if (targetX < 16) targetX = 16;
            if (targetX + width > parent.Bounds.Width - 16) targetX = parent.Bounds.Width - 16 - width;
        }

        // 启用过渡动画并执行移动
        EnableTransitions(control, true);
        control.Margin = new Thickness(targetX, targetY, 0, 0);

        // 等待动画完成
        await Task.Delay(250);

        if (state.DragToken != currentToken) return;

        // 动画完成后固化状态（解决窗口 Resize 时的锚点对齐问题）
        if (state.IsSnappedToEdge)
        {
            EnableTransitions(control, false);
            if (state.IsDockedLeft)
            {
                control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                control.Margin = new Thickness(marginX, targetY, 0, 0);
            }
            else
            {
                control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                control.Margin = new Thickness(0, targetY, marginX, 0);
            }
            // 确保渲染管线在无动画状态下应用了新属性
            await Task.Delay(16);
            EnableTransitions(control, true);
        }
    }
}
