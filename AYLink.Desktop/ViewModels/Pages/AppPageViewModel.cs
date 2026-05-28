using AYLink.Core.Models;
using AYLink.Core.Devices;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 应用管理页 ViewModel - 支持多设备标签页
/// </summary>
public partial class AppPageViewModel : TabbedPageViewModelBase<AppTabViewModel>
{
    public override string PageKey => "App";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("AppPage.Title", "应用管理");
    public override string EmptyStateIcon => "AllApps";
    public override string EmptyStateTitle => Services.Localization.LocalizationManager.Instance.GetString("AppPage.EmptyStateTitle", "未选中设备");
    public override string EmptyStateDescription => Services.Localization.LocalizationManager.Instance.GetString("AppPage.EmptyStateDescription", "请在首页选择一个设备来管理应用");

    protected override AppTabViewModel CreateTab(DeviceModel device) => new(device);

    public override void OnNavigatedTo(object? parameter = null)
    {
        IsActive = true;

        if (parameter is AppNavigationArgs args)
        {
            if (args.RemoteDevice != null && !string.IsNullOrWhiteSpace(args.ServerId))
            {
                AddRemoteTab(args.RemoteDevice, args.ServerId);
                return;
            }

            if (args.Device != null)
            {
                AddNewTabCommand.Execute(args.Device);
                return;
            }
        }

        base.OnNavigatedTo(parameter);
    }

    private void AddRemoteTab(DeviceDescriptor remoteDevice, string serverId)
    {
        var existing = Tabs.FirstOrDefault(tab => tab.RemoteDeviceId == remoteDevice.Id);
        if (existing != null)
        {
            SelectedTab = existing;
            return;
        }

        var runtime = AgentSessionService.Instance.FindServer(serverId)
            ?? throw new System.InvalidOperationException($"未找到远程服务器 {serverId}");
        RegisterTab(new AppTabViewModel(remoteDevice, runtime));
    }

    /// <summary>
    /// 卸载选中应用（右键菜单命令）
    /// </summary>
    [RelayCommand]
    private async Task UninstallApp(AppInfo? app)
    {
        if (app != null && SelectedTab != null)
        {
            await SelectedTab.UninstallAppCommand.ExecuteAsync(app);
        }
    }

    /// <summary>
    /// 启动应用（右键菜单命令）
    /// </summary>
    [RelayCommand]
    private async Task LaunchApp(AppInfo? app)
    {
        if (app != null && SelectedTab != null)
        {
            await SelectedTab.LaunchAppCommand.ExecuteAsync(app);
        }
    }

    /// <summary>
    /// 在新建屏幕中启动应用（右键菜单命令）
    /// </summary>
    [RelayCommand]
    private void LaunchAppNewDisplay(AppInfo? app)
    {
        if (app != null && SelectedTab != null)
        {
            SelectedTab.LaunchAppNewDisplayCommand.Execute(app);
        }
    }

    /// <summary>
    /// 复制包名（右键菜单命令）
    /// </summary>
    [RelayCommand]
    private async Task CopyPackageName(AppInfo? app)
    {
        if (app != null && SelectedTab != null)
        {
            await SelectedTab.CopyPackageNameCommand.ExecuteAsync(app);
        }
    }

    /// <summary>
    /// 打开应用信息（右键菜单命令）
    /// </summary>
    [RelayCommand]
    private async Task OpenAppInfo(AppInfo? app)
    {
        if (app != null && SelectedTab != null)
        {
            await SelectedTab.OpenAppInfoCommand.ExecuteAsync(app);
        }
    }

    [RelayCommand]
    private async Task DownloadApp(AppInfo? app)
    {
        if (app != null && SelectedTab != null)
        {
            await SelectedTab.DownloadAppCommand.ExecuteAsync(app);
        }
    }

    /// <summary>
    /// 刷新应用列表
    /// </summary>
    [RelayCommand]
    private async Task RefreshApps()
    {
        if (SelectedTab != null)
        {
            await SelectedTab.LoadAppsCommand.ExecuteAsync(null);
        }
    }
}
