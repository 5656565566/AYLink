using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using System.Threading.Tasks;

namespace AYLink.Desktop.Views.Dialogs;

public class ProgressDialog
{
    private ContentDialog? _dialog;
    private TextBlock? _messageTextBlock;
    private ProgressBar? _progressBar;

    public Task<ContentDialogResult> ShowAsync(string title, string message, bool isIndeterminate = true)
    {
        _messageTextBlock = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _progressBar = new ProgressBar
        {
            IsIndeterminate = isIndeterminate,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(_messageTextBlock);
        panel.Children.Add(_progressBar);

        _dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = string.Empty // 隐藏关闭按钮 强制等待
        };

        return _dialog.ShowAsync();
    }

    public void UpdateProgress(double value, string? newMessage = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (newMessage != null && _messageTextBlock != null)
            {
                _messageTextBlock.Text = newMessage;
            }
            if (_progressBar != null)
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = value;
            }
        });
    }

    public void Hide()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _dialog?.Hide();
            _dialog = null;
        });
    }
}
