using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Devices;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 设备列表项 ViewModel
/// </summary>
public partial class DeviceItemViewModel(DeviceDescriptor device, System.Func<Task>? refreshAction = null) : ViewModelBase
{
    private readonly DeviceCatalogService _deviceCatalog = DeviceCatalogService.Instance;
    private readonly System.Func<Task>? _refreshAction = refreshAction;

    /// <summary>
    /// 统一设备描述模型
    /// 首页列表改为绑定该模型 而不是直接绑定本地 ADB DeviceModel
    /// </summary>
    [ObservableProperty]
    public partial DeviceDescriptor Device { get; set; } = device;

    public string Name => Device.Name;
    public string Serial => Device.Serial;
    public string ConnectionType => Device.ConnectionType;
    public string SourceName => Device.ProviderName;
    public string StatusText => Device.Status;
    public bool IsConnected => Device.IsConnected;
    public bool IsLocal => Device.SourceKind == DeviceSourceKind.Local;
    public bool IsRemote => Device.SourceKind == DeviceSourceKind.Agent;
    public bool CanMirror => HasCapability(DeviceCapability.Mirror);
    public bool CanOpenFileManager => HasCapability(DeviceCapability.FileManager);
    public bool CanOpenAppManager => HasCapability(DeviceCapability.AppManager);
    public bool CanOpenShell => HasCapability(DeviceCapability.Shell);
    public bool CanOpenDeviceSettings => IsLocal && HasCapability(DeviceCapability.DeviceSettings);
    public bool CanListEncoders => HasCapability(DeviceCapability.ListEncoders);
    public bool CanNewDisplay => HasCapability(DeviceCapability.NewDisplay);
    public bool CanDelete => HasCapability(DeviceCapability.Disconnect);
    public bool CanRename => HasCapability(DeviceCapability.Rename);
    public bool HasRemoteActions => IsRemote && CanRename;

    /// <summary>
    /// 删除设备
    /// 本地设备执行断开 远程设备执行删除
    /// </summary>
    [RelayCommand]
    private async Task DeleteDevice()
    {
        if (!CanDelete)
        {
            return;
        }

        var localizer = Services.Localization.LocalizationManager.Instance;
        var isRemoteDelete = IsRemote;
        var result = await DialogService.ShowMessageAsync(
            isRemoteDelete
                ? localizer.GetString("HomePage.DeleteDevice", "删除设备")
                : localizer.GetString("DeviceItem.ConfirmDisconnectTitle", "确认断开"),
            isRemoteDelete
                ? string.Format(localizer.GetString("HomePage.DeleteSingleMessage", "确定要删除设备 {0} 吗？"), Name)
                : string.Format(localizer.GetString("DeviceItem.ConfirmDisconnectMessage", "确定要断开设备 {0} ({1}) 吗？"), Name, Serial),
            isRemoteDelete
                ? localizer.GetString("HomePage.DeleteDevice", "删除设备")
                : localizer.GetString("DeviceItem.DisconnectButton", "断开"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var success = await _deviceCatalog.DisconnectDeviceAsync(Device);
        if (!success)
        {
            NotificationService.Instance.ShowError(
                isRemoteDelete ? localizer.GetString("HomePage.DeleteDevice", "删除设备") : localizer.GetString("DeviceItem.ConfirmDisconnectTitle", "确认断开"),
                isRemoteDelete
                    ? string.Format(localizer.GetString("HomePage.DeleteFailedMessage", "无法删除设备 {0}"), Name)
                    : string.Format(localizer.GetString("HomePage.DisconnectFailedMessage", "无法断开设备 {0}"), Name));
            return;
        }

        NotificationService.Instance.ShowInfo(
            isRemoteDelete
                ? localizer.GetString("HomePage.DeleteDevice", "删除设备")
                : localizer.GetString("DeviceItem.DisconnectedTitle", "已断开"),
            isRemoteDelete
                ? string.Format(localizer.GetString("HomePage.DeleteSuccessMessage", "设备 {0} 已删除"), Name)
                : string.Format(localizer.GetString("DeviceItem.DisconnectedMessage", "设备 {0} 已断开连接"), Name));

        if (_refreshAction != null)
        {
            await _refreshAction();
        }
    }

    /// <summary>
    /// 重命名设备
    /// </summary>
    [RelayCommand]
    private async Task RenameDevice()
    {
        if (!CanRename)
        {
            return;
        }

        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "name", Watermark = localizer.GetString("ServersPage.DeviceName", "设备名称"), Value = Name, IsRequired = true }
        };

        var (result, data) = await DialogService.ShowInputDialogAsync(
            localizer.GetString("HomePage.RenameDevice", "重命名设备"),
            Serial,
            fields,
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var newName = data.GetValueOrDefault("name", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var updated = await _deviceCatalog.RenameDeviceAsync(Device, newName);
        if (updated == null)
        {
            NotificationService.Instance.ShowError("重命名失败", $"无法更新设备 {Name}");
            return;
        }

        Device = updated;
        NotifyDescriptorChanged();
        if (_refreshAction != null)
        {
            await _refreshAction();
        }
    }

    /// <summary>
    /// 投屏 - 仅本地设备可用
    /// </summary>
    [RelayCommand]
    private async Task Mirror()
    {
        if (IsRemote)
        {
            Navigation.NavigateTo("Screen", new Services.ScreenNavigationArgs
            {
                RemoteDevice = Device,
                ServerId = Device.ProviderId
            });
            return;
        }

        var localDevice = await GetLocalDeviceOrNotifyAsync();
        if (localDevice == null)
        {
            return;
        }

        localDevice.ServerOptions ??= new ServerOptions();
        Navigation.NavigateTo("Screen", localDevice);
    }

    /// <summary>
    /// 文件管理 - 仅本地设备可用
    /// </summary>
    [RelayCommand]
    private async Task OpenFileManager()
    {
        if (IsRemote)
        {
            Navigation.NavigateTo("File", new FileNavigationArgs
            {
                RemoteDevice = Device,
                ServerId = Device.ProviderId
            });
            return;
        }

        var localDevice = await GetLocalDeviceOrNotifyAsync();
        if (localDevice != null)
        {
            Navigation.NavigateTo("File", new FileNavigationArgs
            {
                Device = localDevice
            });
        }
    }

    /// <summary>
    /// 应用管理 - 仅本地设备可用
    /// </summary>
    [RelayCommand]
    private async Task OpenAppManager()
    {
        if (IsRemote)
        {
            Navigation.NavigateTo("App", new AppNavigationArgs
            {
                RemoteDevice = Device,
                ServerId = Device.ProviderId
            });
            return;
        }

        var localDevice = await GetLocalDeviceOrNotifyAsync();
        if (localDevice != null)
        {
            Navigation.NavigateTo("App", new AppNavigationArgs
            {
                Device = localDevice
            });
        }
    }

    /// <summary>
    /// 打开终端 - 仅本地设备可用
    /// </summary>
    [RelayCommand]
    private async Task OpenShell()
    {
        if (IsRemote)
        {
            Navigation.NavigateTo("Shell", new ShellNavigationArgs
            {
                RemoteDevice = Device,
                ServerId = Device.ProviderId
            });
            return;
        }

        var localDevice = await GetLocalDeviceOrNotifyAsync();
        if (localDevice != null)
        {
            Navigation.NavigateTo("Shell", new ShellNavigationArgs
            {
                Device = localDevice
            });
        }
    }

    /// <summary>
    /// 设备设置 - 仅本地设备可用
    /// </summary>
    [RelayCommand]
    private async Task OpenDeviceSettings()
    {
        var localDevice = await GetLocalDeviceOrNotifyAsync();
        if (localDevice != null)
        {
            Navigation.NavigateTo("DeviceSetting", new DeviceSettingNavigationArgs
            {
                DeviceSerial = localDevice.Serial,
                DeviceName = localDevice.Name
            });
        }
    }

    /// <summary>
    /// 查看编码器列表
    /// </summary>
    [RelayCommand]
    private async Task ListEncoders()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var dialog = new Views.Dialogs.ProgressDialog();
        _ = dialog.ShowAsync(
            localizer.GetString("DeviceItem.FetchingEncodersTitle", "获取中"),
            localizer.GetString("DeviceItem.FetchingEncodersMessage", "正在获取设备编码器列表..."),
            isIndeterminate: true);

        try
        {
            IReadOnlyList<string> encoders;
            if (IsRemote)
            {
                encoders = await GetRemoteEncodersAsync();
            }
            else
            {
                var localDevice = await GetLocalDeviceOrNotifyAsync();
                if (localDevice == null)
                {
                    return;
                }

                encoders = await Task.Run(() => (IReadOnlyList<string>)ScrcpyService.Instance.Tool.GetEncoders(localDevice));
            }

            if (encoders.Count > 0)
            {
                await DialogService.ShowMessageAsync(localizer.GetString("DeviceItem.EncoderListTitle", "编码器列表"), string.Join("\n", encoders));
            }
            else
            {
                NotificationService.Instance.ShowWarning(localizer.GetString("Dialog.Tip", "提示"), localizer.GetString("DeviceItem.NoEncodersFound", "未找到可用的编码器"));
            }
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError(
                localizer.GetString("DeviceItem.EncoderListTitle", "编码器列表"),
                ex.Message);
        }
        finally
        {
            dialog.Hide();
        }
    }

    /// <summary>
    /// 新建显示
    /// </summary>
    [RelayCommand]
    private async Task NewDisplay()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new()
            {
                Key = "resolution",
                Label = localizer.GetString("DeviceItem.ResolutionLabel", "分辨率和DPI"),
                Watermark = localizer.GetString("DeviceItem.ResolutionWatermark", "格式: 宽x高/DPI (例如: 1920x1080/420)"),
                Value = "1920x1080/420",
                IsRequired = true
            }
        };

        var (result, data) = await DialogService.ShowInputDialogAsync(
            localizer.GetString("DeviceItem.NewDisplayTitle", "新建虚拟显示器"),
            string.Empty,
            fields,
            localizer.GetString("DeviceItem.CreateButton", "创建"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var inputRes = data.GetValueOrDefault("resolution", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(inputRes))
        {
            return;
        }

        if (IsRemote)
        {
            if (!TryParseNewDisplay(inputRes, out var width, out var height, out var dpi))
            {
                NotificationService.Instance.ShowWarning(
                    localizer.GetString("Dialog.Tip", "提示"),
                    localizer.GetString("DeviceItem.ResolutionWatermark", "格式: 宽x高/DPI (例如: 1920x1080/420)"));
                return;
            }

            Navigation.NavigateTo("Screen", new ScreenNavigationArgs
            {
                RemoteDevice = Device,
                ServerId = Device.ProviderId,
                NewDisplay = true,
                NewDisplayWidth = width,
                NewDisplayHeight = height,
                NewDisplayDpi = dpi
            });
            return;
        }

        var localDevice = await GetLocalDeviceOrNotifyAsync();
        if (localDevice == null)
        {
            return;
        }

        localDevice.ServerOptions ??= new ServerOptions();
        localDevice.ServerOptions.DisplayId = -1;
        localDevice.ServerOptions.NewDisplay = inputRes;
        Navigation.NavigateTo("Screen", localDevice);
    }

    /// <summary>
    /// 检查统一能力位
    /// </summary>
    private bool HasCapability(DeviceCapability capability) => (Device.Capabilities & capability) == capability;

    private async Task<IReadOnlyList<string>> GetRemoteEncodersAsync()
    {
        var runtime = AgentSessionService.Instance.FindServer(Device.ProviderId)
            ?? throw new InvalidOperationException($"未找到远程服务器 {Device.ProviderId}");
        var remoteDeviceId = Device.RemoteDeviceId
            ?? throw new InvalidOperationException("当前远程设备缺少服务端 ID。");
        var accessToken = await runtime.EnsureAccessTokenAsync();
        var encoders = await runtime.Client.GetEncodersAsync(accessToken, remoteDeviceId);
        runtime.TouchSuccess();
        return encoders;
    }

    private static bool TryParseNewDisplay(string value, out int width, out int height, out int dpi)
    {
        width = 0;
        height = 0;
        dpi = 0;

        var segments = value.Trim().Split('/');
        if (segments.Length != 2)
        {
            return false;
        }

        var sizeSegments = segments[0].Split('x', 'X');
        if (sizeSegments.Length != 2)
        {
            return false;
        }

        return int.TryParse(sizeSegments[0], out width) &&
               int.TryParse(sizeSegments[1], out height) &&
               int.TryParse(segments[1], out dpi) &&
               width > 0 &&
               height > 0 &&
               dpi > 0;
    }

    /// <summary>
    /// 将首页统一设备项还原为本地 DeviceModel 以复用现有本地页面链路
    /// 远程设备在首期不走这些深链路
    /// </summary>
    private async Task<DeviceModel?> GetLocalDeviceOrNotifyAsync()
    {
        if (!IsLocal || !_deviceCatalog.TryGetLocalDevice(Device.Id, out var localDevice) || localDevice == null)
        {
            NotificationService.Instance.ShowWarning("暂不支持", "该操作将在后续支持远程设备。");
            return null;
        }

        var online = await _deviceCatalog.IsLocalDeviceOnlineAsync(Device.Id);
        if (!online)
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            NotificationService.Instance.ShowError(
                localizer.GetString("DeviceItem.DeviceOfflineTitle", "设备离线"),
                localizer.GetString("DeviceItem.DeviceOfflineMessage", "无法连接到设备，请检查连接状态"));
            return null;
        }

        return localDevice;
    }

    /// <summary>
    /// 远程设备更新后，通知首页重新读取摘要字段
    /// </summary>
    private void NotifyDescriptorChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Serial));
        OnPropertyChanged(nameof(ConnectionType));
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsConnected));
    }
}
