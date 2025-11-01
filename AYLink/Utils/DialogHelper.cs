using AYLink.ADB;
using AYLink.UI;
using FluentAvalonia.UI.Controls;
using System;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace AYLink.Utils;

public static class DialogHelper
{
    private static ContentDialog? _progressDialog;

    /// <summary>
    /// 显示一个可配置的消息对话框。
    /// </summary>
    /// <param name="title">对话框的标题。</param>
    /// <param name="message">要显示的消息。</param>
    /// <param name="primaryButton">主按钮的文本 (例如 "确定")。</param>
    /// <param name="secondaryButton">次按钮的文本 (例如 "取消", 可选)。</param>
    /// <param name="icon">对话框中显示的图标类型。</param>
    /// <returns>返回用户的选择结果 (Primary 或 Secondary)。</returns>
    public static async Task<ContentDialogResult> MessageShowAsync(
        string title,
        string message,
        string primaryButton = "确定",
        string? secondaryButton = null,
        MessageDialog.MessageDialogIcon icon = MessageDialog.MessageDialogIcon.None)
    {
        var content = new MessageDialog();
        content.Configure(message, icon);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButton,
            DefaultButton = ContentDialogButton.Primary
        };

        if (!string.IsNullOrEmpty(secondaryButton))
        {
            dialog.SecondaryButtonText = secondaryButton;
        }

        return await dialog.ShowAsync();
    }

    /// <summary>
    /// 弹出进度对话框。
    /// </summary>
    /// <param name="title">窗口标题</param>
    /// <param name="message">提示文字</param>
    /// <param name="showProgressBar">
    /// true  → 显示百分比进度条
    /// false → 显示转圈动画
    /// </param>
    /// <param name="onCancel">点击“取消”时的回调（可为 null）</param>
    /// <param name="onRunInBackground">点击“后台运行”时的回调（可为 null）</param>
    /// <returns>对话框关闭后返回的 Task</returns>
    public static ProgressDialog GetProgressShow(
        string title,
        string message,
        bool showProgressBar = false,
        Action? onCancel = null,
        Action? onRunInBackground = null)
    {
        CloseProgress();

        var content = new ProgressDialog
        {
            Message = message,
            ShowProgress = showProgressBar,
            ProgressValue = 0
        };
        content.Initialize(onCancel, onRunInBackground);

        _progressDialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = onRunInBackground == null ? string.Empty : "后台加载",
            IsPrimaryButtonEnabled = onRunInBackground != null,
            CloseButtonText = onCancel == null ? string.Empty : "取消",
            IsSecondaryButtonEnabled = false
        };

        return content;
    }

    public static void ShowProgress()
    {
        if (_progressDialog is null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _progressDialog.ShowAsync();
        });
    }

    
    public static void CloseProgress()
    {
        if (_progressDialog is null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _progressDialog.Hide();
            _progressDialog = null;
        });
    }

    internal static void UpdateProgressMessage(string message)
    {
        if (_progressDialog is null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_progressDialog.Content is ProgressDialog progressDialog)
            {
                progressDialog.Message = message;
            }
        });
    }

    internal static void UpdateProgressValue(double progress)
    {
        if (_progressDialog is null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_progressDialog.Content is ProgressDialog progressDialog)
            {
                progressDialog.ProgressValue = progress;
            }
        });
    }
}