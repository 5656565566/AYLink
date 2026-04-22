using AYLink.Core.ADB;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 首页 ViewModel - 设备列表与连接管理
/// </summary>
public partial class HomePageViewModel : PageViewModelBase
{
    public override string PageKey => "Home";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("HomePage.Title", "首页");

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
    /// </summary>
    [ObservableProperty]
    public partial int SelectedGroupIndex { get; set; }

    /// <summary>
    /// 是否开启多选模式
    /// </summary>
    [ObservableProperty]
    public partial bool IsMultiSelectMode { get; set; }

    // 页面级命令

    /// <summary>
    /// 添加设备命令 - 弹出添加设备对话框
    /// </summary>
    [RelayCommand]
    private async Task AddDevice()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "ip", Watermark = localizer.GetString("HomePage.AddDeviceIpWatermark", "IP 地址 (例如: 127.0.0.1)"), IsRequired = true },
            new() { Key = "port", Watermark = localizer.GetString("HomePage.AddDevicePortWatermark", "端口号 (可选, 默认为 5555)") },
            new() { Key = "pairPort", Watermark = localizer.GetString("HomePage.AddDevicePairPortWatermark", "配对端口 (可选 安卓 无线调试配对 流程)") },
            new() { Key = "pairCode", Watermark = localizer.GetString("HomePage.AddDevicePairCodeWatermark", "配对码（可选）") }
        };

        var (result, data) = await DialogService.ShowInputDialogAsync(
            localizer.GetString("HomePage.AddDeviceTitle", "添加设备"),
            localizer.GetString("HomePage.AddDeviceDescription", "通过网络调试 (Wi-Fi) 连接设备"),
            fields,
            localizer.GetString("HomePage.AddDeviceConnect", "连接"),
            localizer.GetString("HomePage.AddDeviceCancel", "取消")
        );

        if (result == ContentDialogResult.Primary && data != null)
        {
            string ip = data["ip"];
            if (string.IsNullOrWhiteSpace(ip))
            {
                await DialogService.ShowMessageAsync(
                    localizer.GetString("Dialog.Error", "错误"),
                    localizer.GetString("HomePage.AddDeviceIpEmpty", "IP 地址不能为空"));
                return;
            }

            int port = int.TryParse(data["port"], out int tempPort) ? tempPort : 5555;
            string pairPortStr = data["pairPort"];
            string pairCode = data["pairCode"];

            if (int.TryParse(pairPortStr, out int pairPort) && !string.IsNullOrWhiteSpace(pairCode))
            {
                var dialog = new Views.Dialogs.ProgressDialog();
                _ = dialog.ShowAsync(
                    localizer.GetString("HomePage.PairingTitle", "配对中"),
                    string.Format(localizer.GetString("HomePage.PairingMessage", "正在配对设备 {0}:{1}..."), ip, pairPort));
                
                bool pairSuccess = await AdbManager.PairWifiDevice(ip, pairPort, pairCode);
                dialog.Hide();

                if (!pairSuccess)
                {
                    await DialogService.ShowMessageAsync(
                        localizer.GetString("HomePage.PairFailedTitle", "配对失败"),
                        localizer.GetString("HomePage.PairFailedMessage", "请检查配对码和端口是否正确"));
                    return;
                }
            }

            var connectDialog = new Views.Dialogs.ProgressDialog();
            _ = connectDialog.ShowAsync(
                localizer.GetString("HomePage.ConnectingTitle", "连接中"),
                string.Format(localizer.GetString("HomePage.ConnectingMessage", "正在连接到 {0}:{1}..."), ip, port));

            var device = await AdbManager.Instance.ConnectDevice(ip, port);
            
            connectDialog.Hide();

            if (device != null)
            {
                NotificationService.Instance.ShowSuccess(
                    localizer.GetString("HomePage.ConnectSuccessTitle", "连接成功"),
                    string.Format(localizer.GetString("HomePage.ConnectSuccessMessage", "已连接到 {0}:{1}"), ip, port));
                await RefreshDevices();
            }
            else
            {
                await DialogService.ShowMessageAsync(
                    localizer.GetString("HomePage.ConnectFailedTitle", "连接失败"),
                    string.Format(localizer.GetString("HomePage.ConnectFailedMessage", "无法连接到 {0}:{1}，请检查设备是否开启了网络调试"), ip, port));
            }
        }
    }

    /// <summary>
    /// 刷新设备列表命令
    /// </summary>
    [RelayCommand]
    private async Task RefreshDevices()
    {
        // 确保 ADB 服务已启动
        AdbManager.Instance.TryStartAdbServer();

        // 真实逻辑调用 AdbManager
        await AdbManager.Instance.RefreshConnectedDevices();
        var devices = AdbManager.Instance.GetConnectedDevices();
        
        // 更新 UI 集合
        DeviceItems.Clear();
        foreach (var device in devices)
        {
            DeviceItems.Add(new DeviceItemViewModel(device, RefreshDevices));
        }
        
        HasDevices = DeviceItems.Count > 0;
    }
    /// <summary>
    /// 删除设备
    /// </summary>
    /// <param name="selectedItems">被选择的设备</param>
    /// <returns></returns>
    [RelayCommand]
    private async Task DeleteDevice(IList? selectedItems)
    {
        if (selectedItems == null || selectedItems.Count == 0) return;

        // 因为 Avalonia 的 SelectedItems 返回的并非强类型的 List 而是 IList
        var itemsToDelete = new List<DeviceItemViewModel>();
        foreach (var item in selectedItems)
        {
            if (item is DeviceItemViewModel vm)
            {
                itemsToDelete.Add(vm);
            }
        }

        if (itemsToDelete.Count == 0) return;

        var localizer = Services.Localization.LocalizationManager.Instance;
        string message = itemsToDelete.Count == 1
            ? string.Format(localizer.GetString("HomePage.DisconnectSingleMessage", "确定要断开设备 {0} 吗？"), itemsToDelete[0].Name)
            : string.Format(localizer.GetString("HomePage.DisconnectMultipleMessage", "确定要断开选中的 {0} 台设备吗？"), itemsToDelete.Count);

        var result = await DialogService.ShowMessageAsync(
            localizer.GetString("HomePage.DisconnectConfirmTitle", "确认断开"),
            message,
            localizer.GetString("HomePage.DisconnectConfirmButton", "断开"),
            localizer.GetString("Dialog.Cancel", "取消"));
            
        if (result == ContentDialogResult.Primary)
        {
            // 执行断开逻辑
            foreach (var vm in itemsToDelete)
            {
                AdbManager.Instance.DisconnectDevice(vm.Serial);
            }
            NotificationService.Instance.ShowSuccess(
                localizer.GetString("HomePage.DisconnectedTitle", "已断开"),
                string.Format(localizer.GetString("HomePage.DisconnectedMessage", "已断开 {0} 台设备的连接"), itemsToDelete.Count));
            await RefreshDevices();
        }
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
    /// 切换多选模式命令
    /// </summary>
    [RelayCommand]
    private void ToggleMultiSelect()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
        
        string msg = IsMultiSelectMode ? "已开启多选模式" : "已退出多选模式";
        NotificationService.Instance.ShowInfo("状态切换", msg);
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
