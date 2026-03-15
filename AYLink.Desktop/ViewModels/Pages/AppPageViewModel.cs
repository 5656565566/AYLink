using AYLink.Core.Models;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 应用管理页 ViewModel - 支持多设备标签页
/// </summary>
public partial class AppPageViewModel : TabbedPageViewModelBase<AppTabViewModel>
{
    public override string PageKey => "App";
    public override string Title => "应用管理";
    public override string EmptyStateIcon => "AllApps";
    public override string EmptyStateTitle => "未选中设备";
    public override string EmptyStateDescription => "请在首页选择一个设备来管理应用";

    protected override AppTabViewModel CreateTab(DeviceModel device) => new(device);

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
