using Avalonia.Controls;
using AYLink.Desktop.Services;
using AYLink.Desktop.ViewModels;
using FluentAvalonia.UI.Controls;
using System;
using System.Linq;

namespace AYLink.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly NavigationService _navigationService = NavigationService.Instance;

    // 用于防止 ViewModel 导航触发 -> SelectionChanged -> 又触发导航的循环
    private bool _isSyncingSelection;

    public MainWindow()
    {
        InitializeComponent();

        // 订阅导航服务，同步 NavigationView 的选中状态
        _navigationService.Navigated += OnNavigationServiceNavigated;

        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _navigationService.Navigated -= OnNavigationServiceNavigated;
    }

    /// <summary>
    /// NavigationView 选中变化 -> 通知 ViewModel 导航
    /// </summary>
    private void NavView_OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;

        if (DataContext is MainWindowViewModel vm)
        {
            if (e.IsSettingsSelected)
            {
                vm.OnNavItemSelected("Settings");
            }
            else if (e.SelectedItem is NavigationViewItem selectedItem && selectedItem.Tag is string pageKey)
            {
                vm.OnNavItemSelected(pageKey);
            }
        }
    }

    /// <summary>
    /// 导航服务触发 -> 同步 NavigationView 的选中高亮
    /// </summary>
    private void OnNavigationServiceNavigated(string pageKey)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncNavViewSelection(pageKey));
    }

    /// <summary>
    /// 将 NavigationView 的选中项同步到指定 pageKey
    /// </summary>
    private void SyncNavViewSelection(string pageKey)
    {
        _isSyncingSelection = true;
        try
        {
            if (pageKey == "Settings")
            {
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else
            {
                var targetItem = NavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(item => item.Tag is string tag && tag == pageKey);

                if (targetItem != null)
                {
                    NavView.SelectedItem = targetItem;
                }
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }
}
