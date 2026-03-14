using AYLink.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 投屏页 ViewModel - 支持多设备标签页
/// </summary>
public partial class ScreenPageViewModel : PageViewModelBase
{
    public override string PageKey => "Screen";
    public override string Title => "投屏";

    [ObservableProperty]
    private ObservableCollection<ScreenTabViewModel> _tabs = [];

    [ObservableProperty]
    private ScreenTabViewModel? _selectedTab;

    /// <summary>
    /// 添加新标签页
    /// </summary>
    [RelayCommand]
    private void AddNewTab(DeviceModel device)
    {
        var newTab = new ScreenTabViewModel(device);
        newTab.OnCloseRequested += Tab_OnCloseRequested;
        Tabs.Add(newTab);
        SelectedTab = newTab;
    }

    /// <summary>
    /// 添加新标签页（带应用名）
    /// </summary>
    public void AddNewTabWithApp(DeviceModel device, string appName)
    {
        var newTab = new ScreenTabViewModel(device, appName);
        newTab.OnCloseRequested += Tab_OnCloseRequested;
        Tabs.Add(newTab);
        SelectedTab = newTab;
    }

    private void Tab_OnCloseRequested(ScreenTabViewModel tab)
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
            // 每次导航都创建新标签页（投屏可以有多个同设备标签页）
            AddNewTabCommand.Execute(device);
        }
    }
}
