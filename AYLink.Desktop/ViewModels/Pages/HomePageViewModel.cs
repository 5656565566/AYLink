using AYLink.Core.ADB;
using AYLink.Core.Models;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
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
    public override string Title => "首页";

    // 可观察集合

    /// <summary>
    /// 设备列表项（每项包含设备数据 + 操作命令）
    /// </summary>
    public ObservableCollection<DeviceItemViewModel> DeviceItems { get; } = [];

    // 可观察属性

    /// <summary>
    /// 是否有设备连接（控制空状态提示的显示）
    /// </summary>
    [ObservableProperty]
    private bool _hasDevices;

    /// <summary>
    /// 当前选中的设备分组索引
    /// </summary>
    [ObservableProperty]
    private int _selectedGroupIndex;

    /// <summary>
    /// 是否开启多选模式
    /// </summary>
    [ObservableProperty]
    private bool _isMultiSelectMode;

    // 页面级命令

    /// <summary>
    /// 添加设备命令 - 弹出添加设备对话框
    /// </summary>
    [RelayCommand]
    private async Task AddDevice()
    {
        var fields = new List<InputFieldModel>
        {
            new() { Key = "ip", Watermark = "IP 地址 (例如: 127.0.0.1)", IsRequired = true },
            new() { Key = "port", Watermark = "端口号 (可选, 默认为 5555)" },
            new() { Key = "pairPort", Watermark = "配对端口 (可选 安卓 无线调试配对 流程)" },
            new() { Key = "pairCode", Watermark = "配对码（可选）" }
        };

        var (result, data) = await DialogHelper.ShowInputDialogAsync(
            "添加设备",
            "通过网络调试 (Wi-Fi) 连接设备",
            fields,
            "连接",
            "取消"
        );

        if (result == ContentDialogResult.Primary)
        {
            string ip = data["ip"];
            if (string.IsNullOrWhiteSpace(ip))
            {
                await DialogHelper.ShowMessageAsync("错误", "IP 地址不能为空");
                return;
            }

            int port = int.TryParse(data["port"], out int tempPort) ? tempPort : 5555;
            string pairPortStr = data["pairPort"];
            string pairCode = data["pairCode"];

            if (int.TryParse(pairPortStr, out int pairPort) && !string.IsNullOrWhiteSpace(pairCode))
            {
                DialogHelper.ShowProgress("配对中", $"正在配对设备 {ip}:{pairPort}...", isBlocking: true);
                bool pairSuccess = await AdbManager.PairWifiDevice(ip, pairPort, pairCode);
                DialogHelper.CloseProgress();

                if (!pairSuccess)
                {
                    await DialogHelper.ShowMessageAsync("配对失败", "请检查配对码和端口是否正确");
                    return;
                }
            }

            DialogHelper.ShowProgress("连接中", $"正在连接到 {ip}:{port}...", isBlocking: false);

            var device = await AdbManager.Instance.ConnectDevice(ip, port);
            
            DialogHelper.CloseProgress();

            if (device != null)
            {
                DialogHelper.ShowToast("连接成功", $"已连接到 {ip}:{port}", InfoBarSeverity.Success);
                await RefreshDevices();
            }
            else
            {
                await DialogHelper.ShowMessageAsync("连接失败", $"无法连接到 {ip}:{port}，请检查设备是否开启了网络调试");
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

        string message = itemsToDelete.Count == 1
            ? $"确定要断开设备 {itemsToDelete[0].Name} 吗？"
            : $"确定要断开选中的 {itemsToDelete.Count} 台设备吗？";

        var result = await DialogHelper.ShowMessageAsync("确认断开", message, "断开", "取消");
        if (result == ContentDialogResult.Primary)
        {
            // 执行断开逻辑
            foreach (var vm in itemsToDelete)
            {
                AdbManager.Instance.DisconnectDevice(vm.Serial);
            }
            DialogHelper.ShowToast("已断开", $"已断开 {itemsToDelete.Count} 台设备的连接");
            await RefreshDevices();
        }
    }

    /// <summary>
    /// 运行脚本命令
    /// </summary>
    [RelayCommand]
    private void RunScript()
    {
        DialogHelper.ShowToast("提示", "脚本功能尚未实现", InfoBarSeverity.Warning);
    }

    /// <summary>
    /// 同步控制命令
    /// </summary>
    [RelayCommand]
    private void SyncControl()
    {
        DialogHelper.ShowToast("提示", "同步控制功能尚未实现", InfoBarSeverity.Warning);
    }

    /// <summary>
    /// 切换多选模式命令
    /// </summary>
    [RelayCommand]
    private void ToggleMultiSelect()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
        
        string msg = IsMultiSelectMode ? "已开启多选模式" : "已退出多选模式";
        DialogHelper.ShowToast("状态切换", msg, InfoBarSeverity.Informational);
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
