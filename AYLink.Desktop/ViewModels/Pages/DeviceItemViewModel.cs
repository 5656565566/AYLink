using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AYLink.Core.Models;
using AYLink.Desktop.Services;
using System.Threading.Tasks;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.ViewModels.Pages;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 设备列表项 ViewModel - 包装 DeviceModel 并提供设备级操作命令
/// 
/// 在 DataTemplate 中可以直接使用 Compiled Binding：
///   {Binding MirrorCommand}
///   {Binding OpenFileManagerCommand}
/// 无需跨层引用父级 DataContext
/// </summary>
public partial class DeviceItemViewModel(DeviceModel device, System.Func<Task>? refreshAction = null) : ViewModelBase
{
    /// <summary>
    /// 设备数据模型
    /// </summary>
    [ObservableProperty]
    private DeviceModel _device = device;

    // 便捷属性（直接绑定用）
    public string Name => Device.Name;
    public string Serial => Device.Serial;
    public string ConnectionType => Device.ConnectionType;
    public bool IsConnected => Device.IsConnected;

    private readonly System.Func<Task>? _refreshAction = refreshAction;

    // 设备操作命令

    /// <summary>
    /// 删除设备
    /// </summary>
    [RelayCommand]
    private async Task DeleteDevice()
    {
        var result = await DialogHelper.ShowMessageAsync("确认断开", $"确定要断开设备 {Name} ({Serial}) 吗？", "断开", "取消");
        if (result == FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
        {
            Core.ADB.AdbManager.Instance.DisconnectDevice(Serial);
            DialogHelper.ShowToast("已断开", $"设备 {Name} 已断开连接");
            if (_refreshAction != null)
            {
                await _refreshAction();
            }
        }
    }

    private bool CheckDeviceOnline()
    {
        if (!Core.ADB.AdbManager.Instance.IsDeviceTrulyOnline(Device.Serial))
        {
            DialogHelper.ShowToast("设备离线", "无法连接到设备，请检查连接状态", FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 投屏 - 导航到投屏页
    /// </summary>
    [RelayCommand]
    private void Mirror()
    {
        if (!CheckDeviceOnline()) return;

        if (Device.ServerOptions == null)
        {
            Device.ServerOptions = new Core.Scrcpy.ServerOptions();
        }

        Navigation.NavigateTo("Screen", Device);
    }

    /// <summary>
    /// 文件管理 - 导航到文件页
    /// </summary>
    [RelayCommand]
    private void OpenFileManager()
    {
        if (!CheckDeviceOnline()) return;
        Navigation.NavigateTo("File", Device);
    }

    /// <summary>
    /// 应用管理 - 导航到应用页
    /// </summary>
    [RelayCommand]
    private void OpenAppManager()
    {
        if (!CheckDeviceOnline()) return;
        Navigation.NavigateTo("App", Device);
    }

    /// <summary>
    /// 打开终端 - 导航到终端页
    /// </summary>
    [RelayCommand]
    private void OpenShell()
    {
        if (!CheckDeviceOnline()) return;
        Navigation.NavigateTo("Shell", Device);
    }

    /// <summary>
    /// 设备设置 - 导航到设置页
    /// </summary>
    [RelayCommand]
    private void OpenDeviceSettings()
    {
        if (!CheckDeviceOnline()) return;
        Navigation.NavigateTo("DeviceSetting", new DeviceSettingNavigationArgs { DeviceSerial = Device.Serial });
    }

    /// <summary>
    /// 查看编码器列表
    /// </summary>
    [RelayCommand]
    private async Task ListEncoders()
    {
        if (!CheckDeviceOnline()) return;

        DialogHelper.ShowProgress("获取中", "正在获取设备编码器列表...", isBlocking: true);
        
        var tool = new ScrcpyTool(Device, "Scrcpy/scrcpy-server");
        var encoders = await Task.Run(() => tool.GetEncoders());
        
        DialogHelper.CloseProgress();

        if (encoders.Count > 0)
        {
            await DialogHelper.ShowMessageAsync("编码器列表", string.Join("\n", encoders));
        }
        else
        {
            DialogHelper.ShowToast("提示", "未找到可用的编码器", FluentAvalonia.UI.Controls.InfoBarSeverity.Warning);
        }
    }

    /// <summary>
    /// 新建显示 - 创建虚拟显示器投屏
    /// </summary>
    [RelayCommand]
    private void NewDisplay()
    {
        if (!CheckDeviceOnline()) return;

        if (Device.ServerOptions == null)
        {
            Device.ServerOptions = new ServerOptions();
        }
        
        // -1 代表新建显示器
        Device.ServerOptions.DisplayId = -1;
        Device.ServerOptions.NewDisplay = "1920x1080/420";

        Navigation.NavigateTo("Screen", Device);
    }
}
