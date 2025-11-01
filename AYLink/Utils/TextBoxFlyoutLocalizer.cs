using AYLink.UI.Themes;
using AYLink.Utils.Localization;
using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using System;
using System.ComponentModel;
using System.Linq;

namespace AYLink.Utils;

public static class TextBoxFlyoutLocalizer
{

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(TextBoxFlyoutLocalizer));

    private static readonly AttachedProperty<string?> OriginalLabelProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("OriginalLabel", typeof(TextBoxFlyoutLocalizer));


    public static bool GetIsEnabled(TextBox element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(TextBox element, bool value) => element.SetValue(IsEnabledProperty, value);

    static TextBoxFlyoutLocalizer()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            textBox.ContextRequested += OnContextRequested;
        }
        else
        {
            textBox.ContextRequested -= OnContextRequested;
        }
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TextBox { ContextFlyout: { } flyout })
            return;

        flyout.Opened -= OnFlyoutOpened;
        flyout.Opened += OnFlyoutOpened;
    }

    private static void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not TextCommandBarFlyout flyout)
            return;

        foreach (var element in flyout.PrimaryCommands.Concat(flyout.SecondaryCommands))
        {
            if (element is CommandBarButton button)
            {
                var originalLabel = button.GetValue(OriginalLabelProperty);
                if (originalLabel == null)
                {
                    originalLabel = button.Label;
                    button.SetValue(OriginalLabelProperty, originalLabel);
                }

                switch (originalLabel)
                {
                    case "Cut": button.Label = L.Tr("TextControl_Cut"); break;
                    case "Copy": button.Label = L.Tr("TextControl_Copy"); break;
                    case "Paste": button.Label = L.Tr("TextControl_Paste"); break;
                    case "Select All": button.Label = L.Tr("TextControl_SelectAll"); break;
                    case "Undo": button.Label = L.Tr("TextControl_Undo"); break;
                    case "Redo": button.Label = L.Tr("TextControl_Redo"); break;
                }
            }
            else if (element is CommandBarToggleButton toggle)
            {
                var originalLabel = toggle.GetValue(OriginalLabelProperty);
                if (originalLabel == null)
                {
                    originalLabel = toggle.Label;
                    toggle.SetValue(OriginalLabelProperty, originalLabel);
                }

                switch (originalLabel)
                {
                    case "Bold": toggle.Label = L.Tr("TextControl_Bold"); break;
                    case "Italic": toggle.Label = L.Tr("TextControl_Italic"); break;
                    case "Underline": toggle.Label = L.Tr("TextControl_Underline"); break;
                }
            }
        }
    }
}