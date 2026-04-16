using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AYLink.Desktop.Services.Localization;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Services;

/// <summary>
/// 对话与轻提示辅助类 - 统一封装消息框、Toast 与任务型进度提示
/// </summary>
public static class DialogHelper
{
    public interface ITaskContext
    {
        void UpdateProgress(double value, string? newMessage = null);
        void Close(string? completedMessage = null);
        void Fail(string? failedMessage = null);
        void Cancel(string? cancelledMessage = null);
    }

    /// <summary>
    /// 仅使用 Toast 提示的简单上下文（不记录到任务中心）
    /// </summary>
    private class SimpleToastTaskContext : ITaskContext
    {
        private readonly string _title;

        public SimpleToastTaskContext(string title)
        {
            _title = title;
        }

        public void UpdateProgress(double value, string? newMessage = null)
        {
            // 短时任务不需要频繁更新 Toast 进度
        }

        public void Close(string? completedMessage = null)
        {
            ShowToast(_title, completedMessage ?? "操作完成", InfoBarSeverity.Success);
        }

        public void Fail(string? failedMessage = null)
        {
            ShowToast(_title, failedMessage ?? "操作失败", InfoBarSeverity.Error);
        }

        public void Cancel(string? cancelledMessage = null)
        {
            ShowToast(_title, cancelledMessage ?? "操作已取消", InfoBarSeverity.Warning);
        }
    }

    private class ProgressTaskContext : ITaskContext
    {
        public ContentDialog? Dialog { get; set; }
        public ToastModel? Toast { get; set; }
        public ManagedTaskItem? ManagedTask { get; set; }

        public void UpdateProgress(double value, string? newMessage = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Dialog?.Content is StackPanel panel)
                {
                    if (newMessage != null && panel.Children[0] is TextBlock tb)
                    {
                        tb.Text = newMessage;
                    }
                    if (panel.Children[1] is ProgressBar pb)
                    {
                        pb.IsIndeterminate = false;
                        pb.Value = value;
                    }
                }

                if (Toast != null)
                {
                    if (newMessage != null)
                    {
                        Toast.Content = newMessage;
                    }
                    Toast.IsIndeterminate = false;
                    Toast.Progress = value;
                }

                TaskCenterService.Instance.UpdateTask(
                    ManagedTask,
                    progress: value,
                    detail: newMessage,
                    isIndeterminate: false,
                    showProgress: true);
            });
        }

        public void Close(string? completedMessage = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Dialog != null)
                {
                    Dialog.Hide();
                    Dialog = null;
                }

                if (Toast != null)
                {
                    ToastManager.Instance.Dismiss(Toast);
                    Toast = null;
                }

                TaskCenterService.Instance.CompleteTask(ManagedTask, completedMessage);
                ManagedTask = null;
            });
        }

        public void Fail(string? failedMessage = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Dialog != null)
                {
                    Dialog.Hide();
                    Dialog = null;
                }

                if (Toast != null)
                {
                    ToastManager.Instance.Dismiss(Toast);
                    Toast = null;
                }

                TaskCenterService.Instance.FailTask(ManagedTask, failedMessage);
                ManagedTask = null;
            });
        }

        public void Cancel(string? cancelledMessage = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Dialog != null)
                {
                    Dialog.Hide();
                    Dialog = null;
                }

                if (Toast != null)
                {
                    ToastManager.Instance.Dismiss(Toast);
                    Toast = null;
                }

                TaskCenterService.Instance.CancelTask(ManagedTask, cancelledMessage);
                ManagedTask = null;
            });
        }
    }

    /// <summary>
    /// 显示一个阻塞的消息对话框 等待用户确认
    /// </summary>
    public static async Task<ContentDialogResult> ShowMessageAsync(
        string title,
        string message,
        string? primaryButtonText = null,
        string? secondaryButtonText = null,
        string? closeButtonText = null)
    {
        var localizer = LocalizationManager.Instance;
        primaryButtonText ??= localizer.GetString("Dialog.OK", "确定");

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
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

    /// <summary>
    /// 显示一个非阻塞的 Toast 提示
    /// </summary>
    public static void ShowToast(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        ToastManager.Instance.Show(title, message, severity);
    }

    /// <summary>
    /// 显示一个进度提示
    /// 如果 isBlocking 为 true 则显示一个模态对话框阻止用户操作
    /// 如果 isBlocking 为 false 则在右下角显示一个带进度条的 Toast
    /// 同时自动向任务中心注册一个可跟踪任务 供任务页统一管理
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">提示消息</param>
    /// <param name="isBlocking">是否阻塞</param>
    /// <param name="isIndeterminate">进度条样式</param>
    /// <param name="source">任务来源模块</param>
    /// <param name="cancelAction">取消动作</param>
    public static ITaskContext ShowProgress(
        string title,
        string message,
        bool isBlocking = true,
        bool isIndeterminate = true,
        string? source = null,
        Action? cancelAction = null,
        bool showInTaskCenter = true)
    {
        if (!showInTaskCenter)
        {
            // 对于不记录到任务中心的短时任务，我们返回一个不依赖 ManagedTaskItem 的简单上下文，只弹 Toast
            ShowToast(title, message, InfoBarSeverity.Informational);
            return new SimpleToastTaskContext(title);
        }

        var localizer = LocalizationManager.Instance;
        var context = new ProgressTaskContext();

        context.ManagedTask = TaskCenterService.Instance.StartTask(new ManagedTaskOptions
        {
            Title = title,
            Description = message,
            Source = source ?? localizer.GetString("TaskCenterPage.DefaultSource", "通用任务"),
            IsCancelable = cancelAction != null,
            IsIndeterminate = isIndeterminate,
            ShowProgress = true,
            CancelAction = cancelAction
        });

        Dispatcher.UIThread.Post(async () =>
        {
            if (isBlocking)
            {
                var progressBar = new ProgressBar
                {
                    IsIndeterminate = isIndeterminate,
                    Minimum = 0,
                    Maximum = 100,
                    Margin = new Avalonia.Thickness(0, 20, 0, 0)
                };

                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                panel.Children.Add(progressBar);

                context.Dialog = new ContentDialog
                {
                    Title = title,
                    Content = panel,
                    IsPrimaryButtonEnabled = false,
                    IsSecondaryButtonEnabled = false,
                    CloseButtonText = string.Empty // 隐藏关闭按钮 强制等待
                };

                await context.Dialog.ShowAsync();
            }
            else
            {
                context.Toast = ToastManager.Instance.Show(
                    title,
                    message,
                    InfoBarSeverity.Informational,
                    TimeSpan.MaxValue // 不自动关闭
                );
                context.Toast.ShowProgress = true;
                context.Toast.IsIndeterminate = isIndeterminate;
            }
        });

        return context;
    }

    /// <summary>
    /// 显示一个包含多个输入框的对话框
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="description">描述</param>
    /// <param name="fields">输入字段定义集合</param>
    /// <param name="primaryButtonText">确定按钮</param>
    /// <param name="secondaryButtonText">取消按钮</param>
    /// <returns>对话框结果与输入值</returns>
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
