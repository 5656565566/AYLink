using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAvalonia.UI.Controls;
using AYLink.Desktop.Services.Localization;

namespace AYLink.Desktop.Services.Notifications;

public static class DialogService
{
    public static async Task<ContentDialogResult> ShowMessageAsync(
        string title,
        string message,
        string? primaryButtonText = null,
        string? secondaryButtonText = null,
        string? closeButtonText = null)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText ?? "确定",
            DefaultButton = ContentDialogButton.Primary
        };

        if (!string.IsNullOrEmpty(secondaryButtonText))
        {
            dialog.SecondaryButtonText = secondaryButtonText;
        }

        if (!string.IsNullOrEmpty(closeButtonText))
        {
            dialog.CloseButtonText = closeButtonText;
        }

        return await dialog.ShowAsync();
    }

    public static async Task<(ContentDialogResult Result, Dictionary<string, string> Data)> ShowInputDialogAsync(
        string title,
        string description,
        List<Models.InputFieldModel> fields,
        string? primaryButtonText = null,
        string? secondaryButtonText = null)
    {
        var localizer = LocalizationManager.Instance;
        primaryButtonText ??= localizer.GetString("Dialog.OK", "确定");
        secondaryButtonText ??= localizer.GetString("Dialog.Cancel", "取消");

        var inputDialog = new Views.Dialogs.InputDialog(description, fields);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = inputDialog,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = secondaryButtonText,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return (result, inputDialog.GetResults());
    }
}
