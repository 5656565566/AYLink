using FluentAvalonia.UI.Controls;
using AYLink.Desktop.Services;

namespace AYLink.Desktop.Services.Notifications;

public sealed class NotificationService : INotificationService
{
    public static NotificationService Instance { get; } = new();

    private NotificationService()
    {
    }

    public void ShowInfo(string title, string message)
    {
        ToastManager.Instance.Show(title, message, InfoBarSeverity.Informational);
    }

    public void ShowSuccess(string title, string message)
    {
        ToastManager.Instance.Show(title, message, InfoBarSeverity.Success);
    }

    public void ShowWarning(string title, string message)
    {
        ToastManager.Instance.Show(title, message, InfoBarSeverity.Warning);
    }

    public void ShowError(string title, string message)
    {
        ToastManager.Instance.Show(title, message, InfoBarSeverity.Error);
    }
}
