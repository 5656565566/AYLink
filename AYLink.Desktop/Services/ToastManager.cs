using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Services;

public partial class ToastModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private TimeSpan _duration = TimeSpan.FromSeconds(3);

    [ObservableProperty]
    private double _progress = 0;

    [ObservableProperty]
    private bool _showProgress = false;

    [ObservableProperty]
    private bool _isIndeterminate = false;
}

public class ToastManager
{
    public static ToastManager Instance { get; } = new();

    public ObservableCollection<ToastModel> Toasts { get; } = new();

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