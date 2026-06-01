using AYLink.Core.Models;
using AYLink.Core.Devices;
using AYLink.Desktop.Services;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 终端 页 ViewModel - 支持多设备标签页
/// </summary>
public partial class ShellPageViewModel : TabbedPageViewModelBase<ShellTabViewModel>
{
    public override string PageKey => "Shell";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("ShellPage.Title", "终端");
    public override string EmptyStateIcon => "Code";
    public override string EmptyStateTitle => Services.Localization.LocalizationManager.Instance.GetString("ShellPage.EmptyStateTitle", "未选中设备");
    public override string EmptyStateDescription => Services.Localization.LocalizationManager.Instance.GetString("ShellPage.EmptyStateDescription", "请在首页选择一个设备来启动终端");

    protected override ShellTabViewModel CreateTab(DeviceModel device) => new(device);

    public override void OnNavigatedTo(object? parameter = null)
    {
        IsActive = true;

        if (parameter is ShellNavigationArgs args)
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

    protected override void OnTabClosed(ShellTabViewModel tab) { }

    private void AddRemoteTab(DeviceDescriptor remoteDevice, string serverId)
    {
        var existing = Tabs.FirstOrDefault(tab => tab.RemoteDeviceId == remoteDevice.Id);
        if (existing != null)
        {
            SelectedTab = existing;
            return;
        }

        var runtime = Services.AgentSessionService.Instance.FindServer(serverId)
            ?? throw new System.InvalidOperationException($"未找到远程服务器 {serverId}");
        RegisterTab(new ShellTabViewModel(remoteDevice, runtime));
    }
}
