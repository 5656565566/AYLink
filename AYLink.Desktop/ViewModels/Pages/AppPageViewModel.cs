using AYLink.Core.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 应用管理页 ViewModel - 支持多设备标签页
/// </summary>
public partial class AppPageViewModel : PageViewModelBase
{
    public override string PageKey => "App";
    public override string Title => "应用管理";

    [ObservableProperty]
    private ObservableCollection<AppTabViewModel> _tabs = [];

    [ObservableProperty]
    private AppTabViewModel? _selectedTab;

    /// <summary>
    /// 添加新标签页
    /// </summary>
    [RelayCommand]
    private void AddNewTab(DeviceModel? device = null)
    {
        var newTab = new AppTabViewModel(device);
        newTab.OnCloseRequested += Tab_OnCloseRequested;
        Tabs.Add(newTab);
        SelectedTab = newTab;
    }

    private void Tab_OnCloseRequested(AppTabViewModel tab)
    {
        tab.OnCloseRequested -= Tab_OnCloseRequested;
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            // 应用页不需要保留空标签页，直接清空
            SelectedTab = null;
        }
        else if (SelectedTab == null)
        {
            SelectedTab = Tabs.LastOrDefault();
        }
    }

    /// <summary>
    /// 卸载选中应用（右键菜单命令）
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task UninstallApp(AppInfo? app)
    {
        if (app != null && SelectedTab != null)
        {
            await SelectedTab.UninstallAppCommand.ExecuteAsync(app);
        }
    }

    /// <summary>
    /// 刷新应用列表
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshApps()
    {
        if (SelectedTab != null)
        {
            await SelectedTab.LoadAppsCommand.ExecuteAsync(null);
        }
    }

    public override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);

        if (parameter is DeviceModel device)
        {
            // 检查是否已经有该设备的标签页
            var existingTab = Tabs.FirstOrDefault(t => t.Device?.Serial == device.Serial);
            if (existingTab != null)
            {
                SelectedTab = existingTab;
            }
            else
            {
                AddNewTab(device);
            }
        }
    }
}
