using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace AYLink.Controls;

[TemplatePart("PART_CloseButton", typeof(Button))]
[PseudoClasses(":dragging")]
public class BrowserTabItem : TabItem
{
    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<BrowserTabItem, bool>(nameof(IsClosable), true);

    public bool IsClosable
    {
        get => GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
        RoutedEvent.Register<BrowserTabItem, RoutedEventArgs>(nameof(CloseRequested), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> CloseRequested
    {
        add => AddHandler(CloseRequestedEvent, value);
        remove => RemoveHandler(CloseRequestedEvent, value);
    }

    private Button? _closeButton;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_closeButton != null)
        {
            _closeButton.Click -= CloseButton_Click;
        }

        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");

        if (_closeButton != null)
        {
            _closeButton.Click += CloseButton_Click;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
    }

    internal void SetDraggingPseudoClass(bool isDragging)
    {
        PseudoClasses.Set(":dragging", isDragging);
    }
}
