using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AYLink.Core.Devices;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Devices;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 首页 ViewModel - 设备列表与连接管理
/// </summary>
public partial class HomePageViewModel : PageViewModelBase
{
    public override string PageKey => "Home";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("HomePage.Title", "首页");

    private readonly DeviceCatalogService _deviceCatalog = DeviceCatalogService.Instance;
    
    /// <summary>
    /// 设备列表项（每项包含设备数据 + 操作命令）
    /// </summary>
    public ObservableCollection<DeviceItemViewModel> DeviceItems { get; } = [];

    /// <summary>
    /// 是否有设备连接（控制空状态提示的显示）
    /// </summary>
    [ObservableProperty]
    public partial bool HasDevices { get; set; }

    /// <summary>
    /// 当前选中的设备分组索引
    /// 目前仍保留给首页原有 UI 结构使用
    /// </summary>
    [ObservableProperty]
    public partial int SelectedGroupIndex { get; set; }

    public HomePageViewModel()
    {
        _deviceCatalog.DevicesChanged += () =>
        {
            if (IsActive)
            {
                _ = RefreshDevices();
            }
        };
    }

    // 页面级命令

    /// <summary>
    /// 添加设备命令 - 弹出添加设备对话框
    /// 当前入口仍用于本地 ADB 设备 远程设备在服务器页中添加
    /// </summary>
    [RelayCommand]
    private async Task AddDevice()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "name", Watermark = localizer.GetString("ServersPage.DeviceName", "设备名称（可选）") },
            new() { Key = "ip", Watermark = localizer.GetString("HomePage.AddDeviceIpWatermark", "IP 地址 (例如: 127.0.0.1)"), IsRequired = true },
            new() { Key = "port", Watermark = localizer.GetString("HomePage.AddDevicePortWatermark", "端口号 (可选, 默认为 5555)") },
            new() { Key = "pairPort", Watermark = localizer.GetString("HomePage.AddDevicePairPortWatermark", "配对端口 (可选 安卓 无线调试配对 流程)") },
            new() { Key = "pairCode", Watermark = localizer.GetString("HomePage.AddDevicePairCodeWatermark", "配对码（可选）") }
        };

        var (result, data) = await DialogService.ShowInputDialogAsync(
            localizer.GetString("HomePage.AddDeviceTitle", "添加设备"),
            localizer.GetString("HomePage.AddDeviceDescription", "通过网络调试 (Wi-Fi) 连接本地设备。远程设备请到「服务器」页添加。"),
            fields,
            localizer.GetString("HomePage.AddDeviceConnect", "连接"),
            localizer.GetString("HomePage.AddDeviceCancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var ip = data.GetValueOrDefault("ip", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            await DialogService.ShowMessageAsync(localizer.GetString("Dialog.Error", "错误"), localizer.GetString("HomePage.AddDeviceIpEmpty", "IP 地址不能为空"));
            return;
        }

        int.TryParse(data.GetValueOrDefault("port"), out var port);
        int.TryParse(data.GetValueOrDefault("pairPort"), out var pairPort);
        var request = new DeviceCreationRequest
        {
            Name = data.GetValueOrDefault("name", string.Empty).Trim(),
            Host = ip,
            Port = port <= 0 ? 5555 : port,
            PairingPort = pairPort,
            PairingCode = data.GetValueOrDefault("pairCode", string.Empty)
        };

        var device = await _deviceCatalog.AddLocalDeviceAsync(request);
        if (device == null)
        {
            await DialogService.ShowMessageAsync(
                localizer.GetString("HomePage.ConnectFailedTitle", "连接失败"),
                string.Format(localizer.GetString("HomePage.ConnectFailedMessage", "无法连接到 {0}:{1}，请检查设备是否开启了网络调试"), ip, request.Port));
            return;
        }

        NotificationService.Instance.ShowSuccess(
            localizer.GetString("HomePage.ConnectSuccessTitle", "连接成功"),
            string.Format(localizer.GetString("HomePage.ConnectSuccessMessage", "已连接到 {0}:{1}"), ip, request.Port));
        await RefreshDevices();
    }

    /// <summary>
    /// 刷新设备列表命令
    /// 通过统一目录服务聚合本地设备与远程 Agent 设备
    /// </summary>
    [RelayCommand]
    private async Task RefreshDevices()
    {
        var devices = await _deviceCatalog.RefreshAllAsync();
        DeviceItems.Clear();
        foreach (var device in devices)
        {
            DeviceItems.Add(new DeviceItemViewModel(device, RefreshDevices));
        }

        HasDevices = DeviceItems.Count > 0;
    }

    /// <summary>
    /// 删除设备
    /// 当前仅支持批量断开本地设备 远程设备删除仍在服务器侧管理
    /// </summary>
    /// <param name="selectedItems">被选择的设备</param>
    /// <returns></returns>
    [RelayCommand]
    private async Task DeleteDevice(IList? selectedItems)
    {
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        var itemsToDelete = new List<DeviceItemViewModel>();
        foreach (var item in selectedItems)
        {
            if (item is DeviceItemViewModel vm && vm.CanDelete)
            {
                itemsToDelete.Add(vm);
            }
        }

        if (itemsToDelete.Count == 0)
        {
            NotificationService.Instance.ShowWarning("暂不支持", "当前仅支持批量断开本地设备。");
            return;
        }

        var localizer = Services.Localization.LocalizationManager.Instance;
        var message = itemsToDelete.Count == 1
            ? string.Format(localizer.GetString("HomePage.DisconnectSingleMessage", "确定要断开设备 {0} 吗？"), itemsToDelete[0].Name)
            : string.Format(localizer.GetString("HomePage.DisconnectMultipleMessage", "确定要断开选中的 {0} 台设备吗？"), itemsToDelete.Count);

        var result = await DialogService.ShowMessageAsync(
            localizer.GetString("HomePage.DisconnectConfirmTitle", "确认断开"),
            message,
            localizer.GetString("HomePage.DisconnectConfirmButton", "断开"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        foreach (var vm in itemsToDelete)
        {
            await _deviceCatalog.DisconnectLocalDeviceAsync(vm.Device.Id);
        }

        NotificationService.Instance.ShowSuccess(
            localizer.GetString("HomePage.DisconnectedTitle", "已断开"),
            string.Format(localizer.GetString("HomePage.DisconnectedMessage", "已断开 {0} 台设备的连接"), itemsToDelete.Count));
        await RefreshDevices();
    }

    /// <summary>
    /// 运行脚本命令
    /// </summary>
    [RelayCommand]
    private void RunScript()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        NotificationService.Instance.ShowWarning(
            localizer.GetString("Dialog.Tip", "提示"),
            localizer.GetString("HomePage.ScriptNotImplemented", "脚本功能尚未实现"));
    }

    /// <summary>
    /// 同步控制命令
    /// </summary>
    [RelayCommand]
    private void SyncControl()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        NotificationService.Instance.ShowWarning(
            localizer.GetString("Dialog.Tip", "提示"),
            localizer.GetString("HomePage.SyncControlNotImplemented", "同步控制功能尚未实现"));
    }

    /// <summary>
    /// 添加分组命令
    /// </summary>
    [RelayCommand]
    private void AddGroup()
    {
        // TODO: 添加设备分组
    }

    /// <summary>
    /// 删除分组命令
    /// </summary>
    [RelayCommand]
    private void DeleteGroup()
    {
        // TODO: 删除设备分组
    }

    // 生命周期

    public override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);
        // 首次进入自动刷新
        RefreshDevicesCommand.Execute(null);
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
    }
}
