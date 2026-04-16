using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

    public string Name => Device.Name;
    public string Serial => Device.Serial;
    public string ConnectionType => Device.ConnectionType;
    public bool IsConnected => Device.IsConnected;

    private readonly System.Func<Task>? _refreshAction = refreshAction;


    /// <summary>
    /// 删除设备
    /// </summary>
    [RelayCommand]
    private async Task DeleteDevice()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var result = await DialogHelper.ShowMessageAsync(
            localizer.GetString("DeviceItem.ConfirmDisconnectTitle", "确认断开"),
            string.Format(localizer.GetString("DeviceItem.ConfirmDisconnectMessage", "确定要断开设备 {0} ({1}) 吗？"), Name, Serial),
            localizer.GetString("DeviceItem.DisconnectButton", "断开"),
            localizer.GetString("Dialog.Cancel", "取消"));
        if (result == ContentDialogResult.Primary)
        {
            Core.ADB.AdbManager.Instance.DisconnectDevice(Serial);
            DialogHelper.ShowToast(
                localizer.GetString("DeviceItem.DisconnectedTitle", "已断开"),
                string.Format(localizer.GetString("DeviceItem.DisconnectedMessage", "设备 {0} 已断开连接"), Name));
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
            var localizer = Services.Localization.LocalizationManager.Instance;
            DialogHelper.ShowToast(
                localizer.GetString("DeviceItem.DeviceOfflineTitle", "设备离线"),
                localizer.GetString("DeviceItem.DeviceOfflineMessage", "无法连接到设备，请检查连接状态"),
                InfoBarSeverity.Error);
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
            Device.ServerOptions = new ServerOptions();
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
        Navigation.NavigateTo("DeviceSetting", new DeviceSettingNavigationArgs { DeviceSerial = Device.Serial, DeviceName = Device.Name });
    }

    /// <summary>
    /// 查看编码器列表
    /// </summary>
    [RelayCommand]
    private async Task ListEncoders()
    {
        if (!CheckDeviceOnline()) return;

        var localizer = Services.Localization.LocalizationManager.Instance;
        var taskContext = DialogHelper.ShowProgress(
            localizer.GetString("DeviceItem.FetchingEncodersTitle", "获取中"),
            localizer.GetString("DeviceItem.FetchingEncodersMessage", "正在获取设备编码器列表..."),
            isBlocking: true,
            showInTaskCenter: false);

        ScrcpyTool tool = ScrcpyService.Instance.Tool;
        var encoders = await Task.Run(() => tool.GetEncoders(Device));
        
        taskContext.Close();

        if (encoders.Count > 0)
        {
            await DialogHelper.ShowMessageAsync(
                localizer.GetString("DeviceItem.EncoderListTitle", "编码器列表"),
                string.Join("\n", encoders));
        }
        else
        {
            DialogHelper.ShowToast(
                localizer.GetString("Dialog.Tip", "提示"),
                localizer.GetString("DeviceItem.NoEncodersFound", "未找到可用的编码器"),
                InfoBarSeverity.Warning);
        }
    }

    /// <summary>
    /// 新建显示 - 创建虚拟显示器投屏
    /// </summary>
    [RelayCommand]
    private async Task NewDisplay()
    {
        if (!CheckDeviceOnline()) return;

        if (Device.ServerOptions == null)
        {
            Device.ServerOptions = new ServerOptions();
        }

        var localizer = Services.Localization.LocalizationManager.Instance;

        // 定义输入框字段
        var fields = new List<InputFieldModel>
        {
            new() {
                Key = "resolution",
                Label = localizer.GetString("DeviceItem.ResolutionLabel", "分辨率和DPI"),
                Watermark = localizer.GetString("DeviceItem.ResolutionWatermark", "格式: 宽x高/DPI (例如: 1920x1080/420)"),
                Value = "1920x1080/420",
                IsRequired = true
            }
        };

        // 调用对话框
        var (result, data) = await DialogHelper.ShowInputDialogAsync(
            localizer.GetString("DeviceItem.NewDisplayTitle", "新建虚拟显示器"),
            "",
            fields: fields,
            primaryButtonText: localizer.GetString("DeviceItem.CreateButton", "创建")
        );

        // 判断用户是否点击了“确定/创建”按钮
        if (result == ContentDialogResult.Primary && data != null)
        {
            if (data.TryGetValue("resolution", out var inputRes) && !string.IsNullOrWhiteSpace(inputRes))
            {
                // 校验 1920x1080/420 这种格式
                if (!Regex.IsMatch(inputRes, @"^\d+x\d+/\d+$")) {
                    DialogHelper.ShowToast(
                        localizer.GetString("Dialog.Tip", "提示"),
                        localizer.GetString("DeviceItem.InvalidResolution", "错误的分辨率"),
                        InfoBarSeverity.Warning);
                    return;
                }

                Device.ServerOptions.DisplayId = -1; // -1 表示新建
                Device.ServerOptions.NewDisplay = inputRes;

                Navigation.NavigateTo("Screen", Device);
            }
        }
    }
}
