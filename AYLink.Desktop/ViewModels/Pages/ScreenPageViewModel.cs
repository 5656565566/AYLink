using AYLink.Core.Models;
using AYLink.Desktop.Services;
using System;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 投屏页 ViewModel - 支持多设备标签页
/// </summary>
public partial class ScreenPageViewModel : TabbedPageViewModelBase<ScreenTabViewModel>, IDisposable
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

    protected override ScreenTabViewModel CreateTab(DeviceModel device) => new(device);

    protected override void OnTabClosed(ScreenTabViewModel tab) => tab.Dispose();

    public override void OnNavigatedTo(object? parameter = null)
    {
        if (parameter is ScreenNavigationArgs args)
        {
            AddNewTabWithApp(args.Device, args.AppPackageName, args.AppDisplayName);
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
        RegisterTab(newTab);
    }

    /// <summary>
    /// 释放所有标签页资源（关闭 Scrcpy 进程、音频流等）
    /// 在主窗口关闭时调用 防止进程泄露 
    /// </summary>
    public void Dispose()
    {
        foreach (var tab in Tabs.ToArray())
        {
            tab.Dispose();
        }
        Tabs.Clear();
        ForceSetSelectedTab(null);
        GC.SuppressFinalize(this);
    }
}
