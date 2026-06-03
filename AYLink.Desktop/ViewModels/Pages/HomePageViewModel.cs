using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
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

    private List<DeviceDescriptor> _allDevices = [];

    /// <summary>
    /// 首页分组筛选选项
    /// </summary>
    public ObservableCollection<HomeDeviceGroupOptionViewModel> GroupOptions { get; } = [];

    /// <summary>
    /// 根据搜索关键字过滤后的分组筛选选项
    /// </summary>
    public ObservableCollection<HomeDeviceGroupOptionViewModel> FilteredGroupOptions { get; } = [];

    /// <summary>
    /// 是否有设备连接（控制空状态提示的显示）
    /// </summary>
    [ObservableProperty]
    public partial bool HasDevices { get; set; }

    [ObservableProperty]
    public partial HomeDeviceGroupOptionViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial string GroupSearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasFilteredGroupOptions { get; set; } = true;

    public string SelectedGroupDisplayName => SelectedGroup?.DisplayName
        ?? Services.Localization.LocalizationManager.Instance.GetString("HomePage.GroupAllDevices", "全部设备");

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

    partial void OnGroupSearchKeywordChanged(string value)
    {
        ApplyGroupSearchFilter();
    }

    partial void OnSelectedGroupChanged(HomeDeviceGroupOptionViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedGroupDisplayName));
        ApplyDeviceGroupFilter();
        UpdateGroupSelectionState();
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
        _allDevices = devices.ToList();
        RebuildGroupOptions(_allDevices);
        ApplyDeviceGroupFilter();
    }

    private void ApplyDeviceGroupFilter()
    {
        DeviceItems.Clear();
        var selected = SelectedGroup;
        var visibleDevices = selected == null || selected.IsAllDevices
            ? _allDevices
            : _allDevices.Where(device => DeviceMatchesGroup(device, selected)).ToList();

        foreach (var device in visibleDevices)
        {
            DeviceItems.Add(new DeviceItemViewModel(device, RefreshDevices));
        }

        HasDevices = DeviceItems.Count > 0;
    }

    private void RebuildGroupOptions(IReadOnlyList<DeviceDescriptor> devices)
    {
        var selectedKey = SelectedGroup?.Key;
        var allDevices = CreateAllDevicesOption();
        var options = new List<HomeDeviceGroupOptionViewModel> { allDevices };
        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { allDevices.Key };

        foreach (var group in _deviceCatalog.GetLocalDeviceGroups())
        {
            AddGroupOption(options, knownKeys, LocalDeviceProvider.LocalProviderId, "本地", group, SelectGroupOption);
        }

        foreach (var device in devices.Where(item => item.SourceKind != DeviceSourceKind.Local))
        {
            foreach (var group in device.Groups)
            {
                AddGroupOption(options, knownKeys, device.ProviderId, device.ProviderName, group, SelectGroupOption);
            }
        }

        GroupOptions.Clear();
        foreach (var option in options
            .OrderBy(item => item.IsAllDevices ? 0 : 1)
            .ThenBy(item => item.SourceName)
            .ThenBy(item => item.Name))
        {
            GroupOptions.Add(option);
        }

        SelectedGroup = GroupOptions.FirstOrDefault(item => string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
            ?? GroupOptions.FirstOrDefault();
        ApplyGroupSearchFilter();
    }

    private static void AddGroupOption(
        ICollection<HomeDeviceGroupOptionViewModel> options,
        ISet<string> knownKeys,
        string providerId,
        string sourceName,
        DeviceGroupDescriptor group,
        Action<HomeDeviceGroupOptionViewModel> selectAction)
    {
        var key = HomeDeviceGroupOptionViewModel.BuildKey(providerId, group.Id);
        if (!knownKeys.Add(key))
        {
            return;
        }

        options.Add(new HomeDeviceGroupOptionViewModel(providerId, sourceName, group, selectAction));
    }

    private HomeDeviceGroupOptionViewModel CreateAllDevicesOption()
    {
        return HomeDeviceGroupOptionViewModel.CreateAllDevices(
            Services.Localization.LocalizationManager.Instance.GetString("HomePage.GroupAllDevices", "全部设备"),
            SelectGroupOption);
    }

    private void ApplyGroupSearchFilter()
    {
        var keyword = GroupSearchKeyword.Trim();
        FilteredGroupOptions.Clear();
        foreach (var option in GroupOptions)
        {
            if (option.IsAllDevices ||
                string.IsNullOrWhiteSpace(keyword) ||
                option.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                option.SourceName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                option.DisplayName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            {
                FilteredGroupOptions.Add(option);
            }
        }

        HasFilteredGroupOptions = FilteredGroupOptions.Count > 0;
        UpdateGroupSelectionState();
    }

    private void UpdateGroupSelectionState()
    {
        foreach (var option in GroupOptions)
        {
            option.IsSelected = SelectedGroup != null && string.Equals(option.Key, SelectedGroup.Key, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SelectGroupOption(HomeDeviceGroupOptionViewModel option)
    {
        SelectedGroup = option;
        GroupSearchKeyword = string.Empty;
    }

    private static bool DeviceMatchesGroup(DeviceDescriptor device, HomeDeviceGroupOptionViewModel option)
    {
        return string.Equals(device.ProviderId, option.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            device.Groups.Any(group => string.Equals(group.Id, option.GroupId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 删除设备
    /// 本地设备执行断开 远程设备执行删除
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
            return;
        }

        var localizer = Services.Localization.LocalizationManager.Instance;
        var hasRemoteDevice = itemsToDelete.Any(static item => item.IsRemote);
        var hasLocalDevice = itemsToDelete.Any(static item => item.IsLocal);
        var title = hasRemoteDevice
            ? localizer.GetString("HomePage.DeleteDevice", "删除设备")
            : localizer.GetString("HomePage.DisconnectConfirmTitle", "确认断开");
        var confirmText = hasRemoteDevice && !hasLocalDevice
            ? localizer.GetString("HomePage.DeleteDevice", "删除设备")
            : localizer.GetString("HomePage.DisconnectConfirmButton", "断开");
        var message = itemsToDelete.Count == 1
            ? hasRemoteDevice
                ? string.Format(localizer.GetString("HomePage.DeleteSingleMessage", "确定要删除设备 {0} 吗？"), itemsToDelete[0].Name)
                : string.Format(localizer.GetString("HomePage.DisconnectSingleMessage", "确定要断开设备 {0} 吗？"), itemsToDelete[0].Name)
            : hasRemoteDevice
                ? string.Format(localizer.GetString("HomePage.DeleteMultipleMessage", "确定要删除选中的 {0} 台设备吗？"), itemsToDelete.Count)
                : string.Format(localizer.GetString("HomePage.DisconnectMultipleMessage", "确定要断开选中的 {0} 台设备吗？"), itemsToDelete.Count);

        var result = await DialogService.ShowMessageAsync(
            title,
            message,
            confirmText,
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var successCount = 0;
        foreach (var vm in itemsToDelete)
        {
            if (await _deviceCatalog.DisconnectDeviceAsync(vm.Device))
            {
                successCount++;
            }
        }

        if (successCount == 0)
        {
            NotificationService.Instance.ShowError(
                title,
                hasRemoteDevice
                    ? localizer.GetString("HomePage.DeleteFailedBatchMessage", "所选设备删除失败")
                    : localizer.GetString("HomePage.DisconnectFailedBatchMessage", "所选设备断开失败"));
            return;
        }

        NotificationService.Instance.ShowSuccess(
            hasRemoteDevice
                ? localizer.GetString("HomePage.DeleteDevice", "删除设备")
                : localizer.GetString("HomePage.DisconnectedTitle", "已断开"),
            hasRemoteDevice
                ? string.Format(localizer.GetString("HomePage.DeleteSuccessBatchMessage", "已删除 {0} 台设备"), successCount)
                : string.Format(localizer.GetString("HomePage.DisconnectedMessage", "已断开 {0} 台设备的连接"), successCount));
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

/// <summary>
/// 首页设备分组筛选选项
/// </summary>
public partial class HomeDeviceGroupOptionViewModel : ViewModelBase
{
    private readonly Action<HomeDeviceGroupOptionViewModel>? _selectAction;

    private HomeDeviceGroupOptionViewModel(
        string providerId,
        string groupId,
        string sourceName,
        string name,
        bool isAllDevices,
        Action<HomeDeviceGroupOptionViewModel>? selectAction)
    {
        ProviderId = providerId;
        GroupId = groupId;
        SourceName = sourceName;
        Name = name;
        IsAllDevices = isAllDevices;
        _selectAction = selectAction;
        Key = isAllDevices ? "all" : BuildKey(providerId, groupId);
    }

    public HomeDeviceGroupOptionViewModel(
        string providerId,
        string sourceName,
        DeviceGroupDescriptor group,
        Action<HomeDeviceGroupOptionViewModel> selectAction)
        : this(providerId, group.Id, sourceName, group.Name, false, selectAction)
    {
    }

    public string Key { get; }

    public string ProviderId { get; }

    public string GroupId { get; }

    public string SourceName { get; }

    public string Name { get; }

    public bool IsAllDevices { get; }

    public string DisplayName => IsAllDevices ? Name : $"{SourceName} - {Name}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public static string BuildKey(string providerId, string groupId)
        => $"{providerId}:{groupId}";

    public static HomeDeviceGroupOptionViewModel CreateAllDevices(string name, Action<HomeDeviceGroupOptionViewModel> selectAction)
        => new(string.Empty, string.Empty, string.Empty, name, true, selectAction);

    [RelayCommand]
    private void Select()
    {
        _selectAction?.Invoke(this);
    }
}
