using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace AYLink.Controls;

[TemplatePart("PART_CloseButton", typeof(Button))]
[TemplatePart("PART_LeftActionButton", typeof(Button))]
[TemplatePart("PART_LayoutRoot", typeof(Border))]
[PseudoClasses(":dragging", ":has-left-icon")]
public class BrowserTabItem : TabItem
{
    #region Styled Properties

    /// <summary>
    /// 是否可关闭
    /// </summary>
    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<BrowserTabItem, bool>(nameof(IsClosable), true);

    public bool IsClosable
    {
        get => GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>
    /// 左侧图标（如静音图标、音频图标等）
    /// </summary>
    public static readonly StyledProperty<object?> LeftIconProperty =
        AvaloniaProperty.Register<BrowserTabItem, object?>(nameof(LeftIcon));

    public object? LeftIcon
    {
        get => GetValue(LeftIconProperty);
        set => SetValue(LeftIconProperty, value);
    }

    /// <summary>
    /// 左侧图标是否可见
    /// </summary>
    public static readonly StyledProperty<bool> IsLeftIconVisibleProperty =
        AvaloniaProperty.Register<BrowserTabItem, bool>(nameof(IsLeftIconVisible), false);

    public bool IsLeftIconVisible
    {
        get => GetValue(IsLeftIconVisibleProperty);
        set => SetValue(IsLeftIconVisibleProperty, value);
    }

    /// <summary>
    /// 左侧操作按钮的工具提示
    /// </summary>
    public static readonly StyledProperty<string?> LeftActionTooltipProperty =
        AvaloniaProperty.Register<BrowserTabItem, string?>(nameof(LeftActionTooltip));

    public string? LeftActionTooltip
    {
        get => GetValue(LeftActionTooltipProperty);
        set => SetValue(LeftActionTooltipProperty, value);
    }

    #endregion

    #region Routed Events

    public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
        RoutedEvent.Register<BrowserTabItem, RoutedEventArgs>(nameof(CloseRequested), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> CloseRequested
    {
        add => AddHandler(CloseRequestedEvent, value);
        remove => RemoveHandler(CloseRequestedEvent, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> LeftActionClickedEvent =
        RoutedEvent.Register<BrowserTabItem, RoutedEventArgs>(nameof(LeftActionClicked), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> LeftActionClicked
    {
        add => AddHandler(LeftActionClickedEvent, value);
        remove => RemoveHandler(LeftActionClickedEvent, value);
    }

    #endregion

    private Button? _closeButton;
    private Button? _leftActionButton;

    static BrowserTabItem()
    {
        IsLeftIconVisibleProperty.Changed.AddClassHandler<BrowserTabItem>((item, _) =>
        {
            item.UpdateLeftIconPseudoClass();
        });
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 清理旧事件
        if (_closeButton != null)
            _closeButton.Click -= CloseButton_Click;
        if (_leftActionButton != null)
            _leftActionButton.Click -= LeftActionButton_Click;

        // 绑定新模板部件
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        _leftActionButton = e.NameScope.Find<Button>("PART_LeftActionButton");

        if (_closeButton != null)
            _closeButton.Click += CloseButton_Click;
        if (_leftActionButton != null)
            _leftActionButton.Click += LeftActionButton_Click;

        UpdateLeftIconPseudoClass();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
    }

    private void LeftActionButton_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(LeftActionClickedEvent, this));
    }

    /// <summary>
    /// 设置拖拽伪类
    /// </summary>
    internal void SetDraggingPseudoClass(bool isDragging)
    {
        PseudoClasses.Set(":dragging", isDragging);
    }

    private void UpdateLeftIconPseudoClass()
    {
        PseudoClasses.Set(":has-left-icon", IsLeftIconVisible);
    }
}
