using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace AYLink.Controls;

/// <summary>
/// 自定义面板：为标签页提供平滑的位移动画。
/// 在 ArrangeOverride 中计算每个子元素的目标位置，
/// 并通过 RenderTransform + TransformOperationsTransition 实现平滑移动。
/// </summary>
public class AnimatedTabPanel : Panel
{
    /// <summary>
    /// 标签页之间的间距
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AnimatedTabPanel, double>(nameof(Spacing), 1);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    // 记录每个子元素的上一次排列位置，用于计算动画偏移
    private readonly Dictionary<Control, double> _lastArrangedX = new();

    // 当前正在被拖拽的子元素（由外部 TabView 设置）
    private Control? _draggedChild;
    private double _dragOffsetX;

    // 拖拽时被拖拽元素的原始逻辑索引
    private int _draggedOriginalIndex = -1;
    // 拖拽过程中计算出的目标插入索引
    private int _dragTargetIndex = -1;

    /// <summary>
    /// 设置当前正在被拖拽的子元素
    /// </summary>
    public void SetDraggedChild(Control? child, int originalIndex = -1)
    {
        _draggedChild = child;
        _draggedOriginalIndex = originalIndex;
        _dragTargetIndex = originalIndex;
    }

    /// <summary>
    /// 更新拖拽偏移并重新排列（让其他标签让位）
    /// </summary>
    public void UpdateDragOffset(double offsetX)
    {
        _dragOffsetX = offsetX;
        InvalidateArrange();
    }

    /// <summary>
    /// 获取拖拽过程中计算出的目标索引
    /// </summary>
    public int GetDragTargetIndex() => _dragTargetIndex;

    /// <summary>
    /// 清除拖拽状态
    /// </summary>
    public void ClearDragState()
    {
        _draggedChild = null;
        _dragOffsetX = 0;
        _draggedOriginalIndex = -1;
        _dragTargetIndex = -1;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double totalWidth = 0;
        double maxHeight = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(availableSize);
            totalWidth += child.DesiredSize.Width;
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }

        // 加上间距
        var visibleCount = 0;
        foreach (var child in Children)
        {
            if (child.IsVisible) visibleCount++;
        }
        if (visibleCount > 1)
            totalWidth += Spacing * (visibleCount - 1);

        return new Size(totalWidth, maxHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visibleChildren = new List<Control>();
        foreach (var child in Children)
        {
            if (child.IsVisible)
                visibleChildren.Add(child);
        }

        if (visibleChildren.Count == 0)
            return finalSize;

        double spacing = Spacing;

        // 计算每个可见子元素的目标 X 位置（不含拖拽偏移）
        var targetPositions = new Dictionary<Control, double>();
        double currentX = 0;

        if (_draggedChild != null && _draggedOriginalIndex >= 0)
        {
            // 拖拽模式：计算被拖拽元素的当前视觉中心位置
            double draggedWidth = _draggedChild.DesiredSize.Width;
            double draggedVisualCenterX = GetBaseX(_draggedChild, visibleChildren, spacing)
                                          + _dragOffsetX + draggedWidth / 2;

            // 确定目标插入索引
            _dragTargetIndex = _draggedOriginalIndex;
            currentX = 0;

            // 构建不含拖拽元素的子元素列表，按原始顺序
            var otherChildren = new List<Control>();
            foreach (var child in visibleChildren)
            {
                if (child != _draggedChild)
                    otherChildren.Add(child);
            }

            // 计算插入位置：找到拖拽元素应该插入的位置
            int insertIndex = otherChildren.Count; // 默认插在最后
            currentX = 0;
            for (int i = 0; i < otherChildren.Count; i++)
            {
                var child = otherChildren[i];
                double childCenter = currentX + child.DesiredSize.Width / 2;
                if (draggedVisualCenterX < childCenter)
                {
                    insertIndex = i;
                    break;
                }
                currentX += child.DesiredSize.Width + spacing;
            }

            _dragTargetIndex = insertIndex;

            // 按照插入后的顺序排列所有元素
            currentX = 0;
            for (int i = 0; i < otherChildren.Count; i++)
            {
                if (i == insertIndex)
                {
                    // 为拖拽元素预留空间
                    targetPositions[_draggedChild] = currentX;
                    currentX += draggedWidth + spacing;
                }

                var child = otherChildren[i];
                targetPositions[child] = currentX;
                currentX += child.DesiredSize.Width + spacing;
            }

            // 如果插入位置在末尾
            if (insertIndex == otherChildren.Count)
            {
                targetPositions[_draggedChild] = currentX;
                currentX += draggedWidth + spacing;
            }
        }
        else
        {
            // 正常模式：按顺序排列
            foreach (var child in visibleChildren)
            {
                targetPositions[child] = currentX;
                currentX += child.DesiredSize.Width + spacing;
            }
        }

        // 执行排列
        foreach (var child in visibleChildren)
        {
            if (!targetPositions.TryGetValue(child, out double targetX))
                continue;

            double arrangeX;
            if (child == _draggedChild)
            {
                // 被拖拽的元素：使用原始位置 + 拖拽偏移
                double baseX = GetBaseX(child, visibleChildren, spacing);
                arrangeX = baseX;
                // 实际视觉位移通过 RenderTransform 在 TabView 中控制
                child.RenderTransform = new TranslateTransform(_dragOffsetX, 0);
            }
            else
            {
                // 其他元素：排列到目标位置，使用 RenderTransform 做平滑偏移
                arrangeX = targetX;
                // 清除其他元素上的拖拽 RenderTransform
                // 动画由 XAML 中的 TransformOperationsTransition 处理
                child.RenderTransform = null;
            }

            var rect = new Rect(arrangeX, 0, child.DesiredSize.Width, finalSize.Height);
            child.Arrange(rect);

            _lastArrangedX[child] = arrangeX;
        }

        return finalSize;
    }

    /// <summary>
    /// 获取子元素在正常（无拖拽）排列下的 X 位置
    /// </summary>
    private double GetBaseX(Control target, List<Control> visibleChildren, double spacing)
    {
        double x = 0;
        foreach (var child in visibleChildren)
        {
            if (child == target) return x;
            x += child.DesiredSize.Width + spacing;
        }
        return x;
    }
}
