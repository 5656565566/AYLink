using AYLink.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 终端 页 ViewModel - 支持多设备标签页
/// </summary>
public partial class ShellPageViewModel : PageViewModelBase
{
    public override string PageKey => "Shell";
    public override string Title => "终端";

    [ObservableProperty]
    private ObservableCollection<ShellTabViewModel> _tabs = [];

    [ObservableProperty]
    private ShellTabViewModel? _selectedTab;

    /// <summary>
    /// 添加新标签页
    /// </summary>
    [RelayCommand]
    private void AddNewTab(DeviceModel device)
    {
        var newTab = new ShellTabViewModel(device);
        newTab.OnCloseRequested += Tab_OnCloseRequested;
        Tabs.Add(newTab);
        SelectedTab = newTab;
    }

    private void Tab_OnCloseRequested(ShellTabViewModel tab)
    {
        tab.OnCloseRequested -= Tab_OnCloseRequested;
        tab.Dispose();
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            SelectedTab = null;
        }
        else SelectedTab ??= Tabs.LastOrDefault();
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
                AddNewTabCommand.Execute(device);
            }
        }
    }
}
