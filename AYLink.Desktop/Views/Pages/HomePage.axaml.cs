using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AYLink.Desktop.ViewModels.Pages;

namespace AYLink.Desktop.Views.Pages;

public partial class HomePage : UserControl
{
    private bool _isGroupPickerOpen;

    public HomePage()
    {
        InitializeComponent();
    }

    private void GroupPickerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isGroupPickerOpen)
        {
            FlyoutBase.GetAttachedFlyout(GroupPickerButton)?.Hide();
            e.Handled = true;
            return;
        }

        FlyoutBase.ShowAttachedFlyout(GroupPickerButton);
        e.Handled = true;
    }

    private void GroupPickerFlyout_Opened(object? sender, System.EventArgs e)
    {
        _isGroupPickerOpen = true;
    }

    private void GroupPickerFlyout_Closed(object? sender, System.EventArgs e)
    {
        _isGroupPickerOpen = false;
    }

    private void GroupOptionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HomeDeviceGroupOptionViewModel option } &&
            DataContext is HomePageViewModel viewModel)
        {
            viewModel.SelectGroupOption(option);
        }

        Dispatcher.UIThread.Post(() =>
        {
            FlyoutBase.GetAttachedFlyout(GroupPickerButton)?.Hide();
        });
        e.Handled = true;
    }
}
