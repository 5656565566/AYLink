using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Services;

public static class DialogHelper
{
    /// <summary>
    /// 显示一个阻塞的消息对话框，等待用户确认
    /// </summary>
    public static async Task<ContentDialogResult> ShowMessageAsync(
        string title,
        string message,
        string primaryButtonText = "确定",
        string? secondaryButtonText = null,
        string? closeButtonText = null)
    {
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
    /// 显示一个非阻塞的 Toast 提示 (缩小版的对话框)
    /// </summary>
    public static void ShowToast(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        ToastManager.Instance.Show(title, message, severity);
    }

    private static ContentDialog? _currentProgressDialog;
    private static ToastModel? _currentProgressToast;

    /// <summary>
    /// 显示一个进度提示。
    /// 如果 isBlocking 为 true，则显示一个模态对话框阻止用户操作。
    /// 如果 isBlocking 为 false，则在右下角显示一个带进度条的 Toast。
    /// </summary>
    public static void ShowProgress(string title, string message, bool isBlocking = true, bool isIndeterminate = true)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (isBlocking)
            {
                if (_currentProgressDialog != null) return;

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

                _currentProgressDialog = new ContentDialog
                {
                    Title = title,
                    Content = panel,
                    IsPrimaryButtonEnabled = false,
                    IsSecondaryButtonEnabled = false,
                    CloseButtonText = string.Empty // 隐藏关闭按钮，强制等待
                };

                await _currentProgressDialog.ShowAsync();
            }
            else
            {
                if (_currentProgressToast != null) return;

                _currentProgressToast = ToastManager.Instance.Show(
                    title, 
                    message, 
                    InfoBarSeverity.Informational, 
                    TimeSpan.MaxValue // 不自动关闭
                );
                _currentProgressToast.ShowProgress = true;
                _currentProgressToast.IsIndeterminate = isIndeterminate;
            }
        });
    }

    /// <summary>
    /// 更新进度条的值 (0-100) 和消息
    /// </summary>
    public static void UpdateProgress(double value, string? newMessage = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_currentProgressDialog?.Content is StackPanel panel)
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

            if (_currentProgressToast != null)
            {
                if (newMessage != null)
                {
                    _currentProgressToast.Content = newMessage;
                }
                _currentProgressToast.IsIndeterminate = false;
                _currentProgressToast.Progress = value;
            }
        });
    }

    /// <summary>
    /// 显示一个包含多个输入框的对话框
    /// </summary>
    public static async Task<(ContentDialogResult Result, System.Collections.Generic.Dictionary<string, string> Data)> ShowInputDialogAsync(
        string title,
        string description,
        System.Collections.Generic.List<AYLink.Desktop.Models.InputFieldModel> fields,
        string primaryButtonText = "确定",
        string secondaryButtonText = "取消")
    {
        var inputDialog = new AYLink.Desktop.Views.Dialogs.InputDialog(description, fields);

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

    /// <summary>
    /// 关闭当前的进度提示
    /// </summary>
    public static void CloseProgress()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_currentProgressDialog != null)
            {
                _currentProgressDialog.Hide();
                _currentProgressDialog = null;
            }

            if (_currentProgressToast != null)
            {
                ToastManager.Instance.Dismiss(_currentProgressToast);
                _currentProgressToast = null;
            }
        });
    }
}