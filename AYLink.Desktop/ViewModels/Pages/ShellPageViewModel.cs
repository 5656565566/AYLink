using AYLink.Core.Models;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 终端 页 ViewModel - 支持多设备标签页
/// </summary>
public partial class ShellPageViewModel : TabbedPageViewModelBase<ShellTabViewModel>
{
    public override string PageKey => "Shell";
    public override string Title => "终端";
    public override string EmptyStateIcon => "Code";
    public override string EmptyStateTitle => "未选中设备";
    public override string EmptyStateDescription => "请在首页选择一个设备来启动终端";

    protected override ShellTabViewModel CreateTab(DeviceModel device) => new(device);

    protected override void OnTabClosed(ShellTabViewModel tab) => tab.Dispose();
}
