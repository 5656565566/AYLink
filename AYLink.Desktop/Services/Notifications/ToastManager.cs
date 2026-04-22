using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Services.Notifications;

public partial class ToastModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3);

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool ShowProgress { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }
}

public sealed class ToastManager
{
    public static ToastManager Instance { get; } = new();

    public ObservableCollection<ToastModel> Toasts { get; } = new();

    private ToastManager()
    {
    }

    public ToastModel Show(string title, string content, InfoBarSeverity severity = InfoBarSeverity.Informational, TimeSpan? duration = null)
    {
        var toast = new ToastModel
        {
            Title = title,
            Content = content,
            Severity = severity,
            Duration = duration ?? TimeSpan.FromSeconds(3)
        };

        Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);

            if (toast.Duration != TimeSpan.Zero && toast.Duration != TimeSpan.MaxValue)
            {
                Task.Delay(toast.Duration).ContinueWith(_ =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Toasts.Remove(toast);
                    });
                });
            }
        });

        return toast;
    }

    public void Dismiss(ToastModel toast)
    {
        Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
    }
}
