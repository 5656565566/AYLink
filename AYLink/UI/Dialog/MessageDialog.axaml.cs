using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace AYLink.UI;

public partial class MessageDialog : UserControl
{
    /// <summary>
    /// ﭘ۷ﺻﮒﺵﻳﺵ۱ﭘﺿﭨﺍﺟﮨﺟﺭﺻﺿﺵﺿﮌﺝﭖﺥﺱﺙﺎﻡﭺﻓﺷﺱ
    /// </summary>
    public enum MessageDialogIcon
    {
        None,         // ﺎﭨﺵﺿﮌﺝﺱﺙﺎﻡ
        Information,  // ﺷﺧﺵ۱
        Success,      // ﺏﺭﺗ۵
        Warning,      // ﺝﺁﺕﮔ
        Error         // ﺑﻥﺳﮩ
    }

    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ﺧﻛﻅﺣﭘﺿﭨﺍﺟﮨﭖﺥﺥﻌﺫﻏﭦﺱﺱﺙﺎﻡ
    /// </summary>
    /// <param name="message">ﺻ۹ﺵﺿﮌﺝﭖﺥﺵﻳﺵ۱ﺳﺥﺎﺝ</param>
    /// <param name="icon">ﺻ۹ﺵﺿﮌﺝﭖﺥﺱﺙﺎﻡﭺﻓﺷﺱ</param>
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