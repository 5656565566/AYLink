using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.ADB;
using AYLink.Core.Models;

namespace AYLink.Core.Devices;

/// <summary>
/// 本地设备 Provider
/// 负责将现有本地 ADB 能力适配到统一设备 Provider 接口
/// </summary>
public sealed class LocalDeviceProvider : IDeviceProvider
{
    /// <summary>
    /// 本地 Provider 固定标识
    /// </summary>
    public const string LocalProviderId = "local";

    /// <summary>
    /// 当前 Provider 的唯一标识
    /// </summary>
    public string ProviderId => LocalProviderId;

    /// <summary>
    /// 当前 Provider 的显示名称
    /// </summary>
    public string DisplayName => "本地设备";

    /// <summary>
    /// 当前 Provider 对应的设备来源类型
    /// </summary>
    public DeviceSourceKind SourceKind => DeviceSourceKind.Local;

    /// <summary>
    /// 刷新本地已连接设备并返回统一设备描述集合
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本地设备描述列表</returns>
    public async Task<IReadOnlyList<DeviceDescriptor>> RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AdbManager.Instance.TryStartAdbServer();
        await AdbManager.Instance.RefreshConnectedDevices();
        return AdbManager.Instance.GetConnectedDevices()
            .Select(Map)
            .ToList();
    }

    /// <summary>
    /// 通过主机地址和端口新增本地网络调试设备
    /// 如提供配对信息则会先执行无线调试配对
    /// </summary>
    /// <param name="request">设备接入请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>接入成功后的设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> AddDeviceAsync(DeviceCreationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var host = request.Host.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        if (request.PairingPort > 0 && !string.IsNullOrWhiteSpace(request.PairingCode))
        {
            var paired = await AdbManager.PairWifiDevice(host, request.PairingPort, request.PairingCode.Trim());
            if (!paired)
            {
                return null;
            }
        }

        var device = await AdbManager.Instance.ConnectDevice(host, request.Port <= 0 ? 5555 : request.Port);
        return device == null ? null : Map(device);
    }

    /// <summary>
    /// 按设备标识连接本地设备
    /// 支持以 host:port 形式重新发起连接 也支持直接返回已存在的设备描述
    /// </summary>
    /// <param name="deviceId">本地设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接后的设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> ConnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        var parts = deviceId.Split(':', 2);
        if (parts.Length == 2 && int.TryParse(parts[1], out var port))
        {
            var device = await AdbManager.Instance.ConnectDevice(parts[0], port);
            return device == null ? null : Map(device);
        }

        var existing = AdbManager.Instance.GetDeviceBySerial(deviceId);
        return existing == null ? null : Map(existing);
    }

    /// <summary>
    /// 本地 Provider 当前未实现设备重命名
    /// </summary>
    /// <param name="deviceId">本地设备标识</param>
    /// <param name="newName">新的设备名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>始终返回 null</returns>
    public Task<DeviceDescriptor?> RenameDeviceAsync(string deviceId, string newName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<DeviceDescriptor?>(null);
    }

    /// <summary>
    /// 断开指定本地设备连接
    /// </summary>
    /// <param name="deviceId">本地设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作是否成功进入断开流程</returns>
    public Task<bool> DisconnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Task.FromResult(false);
        }

        AdbManager.Instance.DisconnectDevice(deviceId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 检查本地设备是否真实在线
    /// </summary>
    /// <param name="deviceId">本地设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备是否在线可用</returns>
    public Task<bool> IsDeviceOnlineAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AdbManager.Instance.IsDeviceTrulyOnline(deviceId));
    }

    /// <summary>
    /// 尝试根据统一设备标识取回本地 DeviceModel
    /// 供现有本地投屏、文件、终端等深链路复用
    /// </summary>
    /// <param name="deviceId">本地设备标识</param>
    /// <param name="deviceModel">匹配到的本地设备模型</param>
    /// <returns>是否成功获取本地设备模型</returns>
    public bool TryGetLocalDevice(string deviceId, out DeviceModel? deviceModel)
    {
        deviceModel = AdbManager.Instance.GetDeviceBySerial(deviceId);
        return deviceModel != null;
    }

    /// <summary>
    /// 将本地 DeviceModel 转换为统一设备描述
    /// </summary>
    /// <param name="device">本地设备模型</param>
    /// <returns>统一设备描述</returns>
    private DeviceDescriptor Map(DeviceModel device)
    {
        return new DeviceDescriptor
        {
            Id = device.Serial,
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            SourceKind = DeviceSourceKind.Local,
            Name = device.Name,
            Serial = device.Serial,
            ConnectionType = device.ConnectionType,
            Status = device.IsConnected ? "online" : "offline",
            IsConnected = device.IsConnected,
            Capabilities =
                DeviceCapability.Mirror |
                DeviceCapability.FileManager |
                DeviceCapability.AppManager |
                DeviceCapability.Shell |
                DeviceCapability.DeviceSettings |
                DeviceCapability.ListEncoders |
                DeviceCapability.NewDisplay |
                DeviceCapability.Disconnect
        };
    }
}
