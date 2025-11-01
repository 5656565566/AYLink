using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace AYLink.UI;

public partial class MessageDialog : UserControl
{
    /// <summary>
    /// 定义消息对话框可以显示的图标类型。
    /// </summary>
    public enum MessageDialogIcon
    {
        None,         // 不显示图标
        Information,  // 信息
        Success,      // 成功
        Warning,      // 警告
        Error         // 错误
    }

    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 配置对话框的内容和图标。
    /// </summary>
    /// <param name="message">要显示的消息文本。</param>
    /// <param name="icon">要显示的图标类型。</param>
    public void Configure(string message, MessageDialogIcon icon)
    {
        MessageTextBlock.Text = message;

        switch (icon)
        {
            case MessageDialogIcon.Information:
                DialogIcon.Symbol = Symbol.Alert;
                IconBackground.Background = (IBrush)Application.Current!.FindResource("SystemFillColorInfoBrush")!;
                break;

            case MessageDialogIcon.Success:
                DialogIcon.Symbol = Symbol.Accept;
                IconBackground.Background = (IBrush)Application.Current!.FindResource("SystemFillColorSuccessBrush")!;
                break;

            case MessageDialogIcon.Warning:
                DialogIcon.Symbol = Symbol.Important;
                IconBackground.Background = (IBrush)Application.Current!.FindResource("SystemFillColorCautionBrush")!;
                break;

            case MessageDialogIcon.Error:
                DialogIcon.Symbol = Symbol.Dismiss;
                IconBackground.Background = (IBrush)Application.Current!.FindResource("SystemFillColorCriticalBrush")!;
                break;

            case MessageDialogIcon.None:
            default:
                IconBackground.IsVisible = false;
                break;
        }
    }
}