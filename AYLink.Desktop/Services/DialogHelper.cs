using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AYLink.Desktop.Services.Localization;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Services;

public static class DialogHelper
{
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
    /// 显示一个非阻塞的 Toast 提示 (缩小版的对话框)
    /// </summary>
    public static void ShowToast(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        ToastManager.Instance.Show(title, message, severity);
    }

    private static ContentDialog? _currentProgressDialog;
    private static ToastModel? _currentProgressToast;

    /// <summary>
    /// 显示一个进度提示
    /// 如果 isBlocking 为 true 则显示一个模态对话框阻止用户操作
    /// 如果 isBlocking 为 false 则在右下角显示一个带进度条的 Toast
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">提示消息</param>
    /// <param name="isBlocking">是否阻塞</param>
    /// <param name="isIndeterminate">进度条样式</param>
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
                    CloseButtonText = string.Empty // 隐藏关闭按钮 强制等待
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
    /// <param name="value">更新的数值</param>
    /// <param name="newMessage">更新的提示消息</param>
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
    /// <param name="title">标题</param>
    /// <param name="description">描述</param>
    /// <param name="fields"></param>
    /// <param name="primaryButtonText">确定按钮</param>
    /// <param name="secondaryButtonText">取消按钮</param>
    /// <returns></returns>
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