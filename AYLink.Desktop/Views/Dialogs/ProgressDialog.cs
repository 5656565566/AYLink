using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Views.Dialogs;

public sealed class ProgressDialogOptions
{
    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public bool IsIndeterminate { get; init; } = true;

    public bool IsCancelable { get; init; }

    public string CancelText { get; init; } = string.Empty;

    public bool CloseOnCancelRequested { get; init; }

    public CancellationTokenSource? CancellationTokenSource { get; init; }

    public Action? OnCancel { get; init; }
}

public class ProgressDialog
{
    private readonly object _syncRoot = new();
    private ContentDialog? _dialog;
    private TextBlock? _messageTextBlock;
    private ProgressBar? _progressBar;
    private ProgressDialogOptions? _options;
    private bool _isShown;
    private bool _isClosing;
    private bool _cancelRequested;

    public Task<ContentDialogResult> ShowAsync(string title, string message, bool isIndeterminate = true)
    {
        return ShowAsync(new ProgressDialogOptions
        {
            Title = title,
            Message = message,
            IsIndeterminate = isIndeterminate
        });
    }

    public async Task<ContentDialogResult> ShowAsync(ProgressDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_isShown && !_isClosing)
        {
            throw new InvalidOperationException("ProgressDialog is already shown.");
        }

        _options = options;
        _cancelRequested = false;
        _isClosing = false;

        _messageTextBlock = new TextBlock
        {
            Text = options.Message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        _progressBar = new ProgressBar
        {
            IsIndeterminate = options.IsIndeterminate,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(_messageTextBlock);
        panel.Children.Add(_progressBar);

        _dialog = new ContentDialog
        {
            Title = options.Title,
            Content = panel,
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = options.IsCancelable
                ? (string.IsNullOrWhiteSpace(options.CancelText) ? "取消" : options.CancelText)
                : string.Empty
        };

        if (options.IsCancelable)
        {
            _dialog.CloseButtonCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(RequestCancel, CanRequestCancel);
        }

        _isShown = true;

        try
        {
            return await _dialog.ShowAsync();
        }
        finally
        {
            lock (_syncRoot)
            {
                _isShown = false;
                _isClosing = false;
                _dialog = null;
            }
        }
    }

    public void UpdateProgress(double value, string? newMessage = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isClosing || _dialog == null)
            {
                return;
            }

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
            if (_dialog == null || _isClosing)
            {
                return;
            }

            _isClosing = true;
            _dialog.Hide();
        });
    }

    public void RequestCancel()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!CanRequestCancel())
            {
                return;
            }

            _cancelRequested = true;
            _options?.CancellationTokenSource?.Cancel();
            _options?.OnCancel?.Invoke();

            _messageTextBlock?.Text = "正在取消...";

            _dialog?.CloseButtonText = null;

            if (_options?.CloseOnCancelRequested == true)
            {
                Hide();
            }
        });
    }

    public bool CanRequestCancel()
    {
        return _dialog != null
            && _options?.IsCancelable == true
            && !_cancelRequested
            && !_isClosing;
    }
}
