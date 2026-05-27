using AYLink.Core.Models;
using AYLink.Desktop.Services;
using System;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 投屏页 ViewModel - 支持多设备标签页
/// </summary>
public partial class ScreenPageViewModel : TabbedPageViewModelBase<ScreenTabViewModel>
{
    public override string PageKey => "Screen";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("ScreenPage.Title", "投屏");
    public override string EmptyStateIcon => "Play";
    public override string EmptyStateTitle => Services.Localization.LocalizationManager.Instance.GetString("ScreenPage.EmptyStateTitle", "未选中设备");
    public override string EmptyStateDescription => Services.Localization.LocalizationManager.Instance.GetString("ScreenPage.EmptyStateDescription", "请在首页选择一个设备来启动投屏");

    /// <summary>
    /// 投屏允许同设备多开标签
    /// </summary>
    protected override bool AllowDuplicateDeviceTabs => true;

    protected override ScreenTabViewModel CreateTab(DeviceModel device)
    {
        var tab = new ScreenTabViewModel(device);
        ConfigureTab(tab);
        return tab;
    }

    protected override void OnTabClosed(ScreenTabViewModel tab) { }

    public override void OnNavigatedTo(object? parameter = null)
    {
        if (parameter is ScreenNavigationArgs args)
        {
            if (args.RemoteDevice != null && !string.IsNullOrWhiteSpace(args.ServerId))
            {
                AddRemoteTab(args.RemoteDevice, args.ServerId, args.AppPackageName, args.AppDisplayName);
                IsActive = true;
                return;
            }

            if (args.Device != null)
            {
                AddNewTabWithApp(args.Device, args.AppPackageName, args.AppDisplayName);
            }
            IsActive = true;
            return;
        }

        base.OnNavigatedTo(parameter);
    }

    /// <summary>
    /// 添加新标签页（带应用启动信息）- 使用基类 RegisterTab 统一注册
    /// </summary>
    public void AddNewTabWithApp(DeviceModel device, string? appPackageName, string? appDisplayName)
    {
        var newTab = new ScreenTabViewModel(device, appPackageName, appDisplayName);
        ConfigureTab(newTab);
        RegisterTab(newTab);
    }

    /// <summary>
    /// 添加远程 Agent 投屏标签页
    /// </summary>
    /// <param name="remoteDevice">远程设备摘要</param>
    /// <param name="serverId">所属服务器标识</param>
    /// <param name="appPackageName">可选应用包名</param>
    /// <param name="appDisplayName">可选应用显示名称</param>
    public void AddRemoteTab(AYLink.Core.Devices.DeviceDescriptor remoteDevice, string serverId, string? appPackageName, string? appDisplayName)
    {
        var server = AgentSessionService.Instance.FindServer(serverId);
        if (server == null)
        {
            throw new InvalidOperationException($"未找到远程服务器 {serverId}");
        }

        var newTab = new ScreenTabViewModel(remoteDevice, server, appPackageName, appDisplayName);
        ConfigureTab(newTab);
        RegisterTab(newTab);
    }

    private void ConfigureTab(ScreenTabViewModel tab)
    {
        tab.SetKeyboardInputGate(() => IsActive && SelectedTab == tab);
    }

    /// <summary>
    /// 释放所有标签页资源（关闭 Scrcpy 进程、音频流等）
    /// 在主窗口关闭时调用 防止进程泄露 
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
