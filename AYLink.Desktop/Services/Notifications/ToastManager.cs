using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>
    /// 是否显示取消按钮
    /// </summary>
    [ObservableProperty]
    public partial bool IsCancelable { get; set; }

    /// <summary>
    /// 取消操作的命令
    /// </summary>
    public ICommand? CancelCommand { get; set; }
}

public sealed class ToastManager
{
    public static ToastManager Instance { get; } = new();

    private readonly IUiDispatcher _uiDispatcher = UiDispatcher.Instance;

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

        _uiDispatcher.Post(() =>
        {
            Toasts.Add(toast);

            if (toast.Duration != TimeSpan.Zero && toast.Duration != TimeSpan.MaxValue)
            {
                Task.Delay(toast.Duration).ContinueWith(_ =>
                {
                    _uiDispatcher.Post(() =>
                    {
                        Toasts.Remove(toast);
                    });
                });
            }
        });

        return toast;
    }

    public void Update(ToastModel toast, Action<ToastModel> update)
    {
        ArgumentNullException.ThrowIfNull(toast);
        ArgumentNullException.ThrowIfNull(update);

        _uiDispatcher.Post(() => update(toast));
    }

    /// <summary>
    /// 显示带进度条的持久 Toast
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="content">内容</param>
    /// <param name="severity">严重性</param>
    /// <param name="isIndeterminate">是否为不确定进度</param>
    /// <param name="cancelAction">取消回调 不为 null 则显示取消按钮</param>
    /// <returns>可用于更新进度的 ToastModel</returns>
    public ToastModel ShowProgress(string title, string content,
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        bool isIndeterminate = true,
        Action? cancelAction = null)
    {
        var toast = new ToastModel
        {
            Title = title,
            Content = content,
            Severity = severity,
            Duration = TimeSpan.MaxValue, // 持久显示
            ShowProgress = true,
            IsIndeterminate = isIndeterminate,
            IsCancelable = cancelAction != null,
        };

        if (cancelAction != null)
        {
            toast.CancelCommand = new RelayCommand(() =>
            {
                cancelAction();
                Update(toast, currentToast =>
                {
                    currentToast.IsCancelable = false;
                    currentToast.Content = Localization.LocalizationManager.Instance
                        .GetString("Toast.Cancelling", "正在取消...");
                });
            });
        }

        _uiDispatcher.Post(() => Toasts.Add(toast));
        return toast;
    }

    public void Dismiss(ToastModel toast)
    {
        _uiDispatcher.Post(() => Toasts.Remove(toast));
    }
}
