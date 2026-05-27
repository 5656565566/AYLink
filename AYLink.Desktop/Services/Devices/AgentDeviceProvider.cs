using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Agent;
using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Desktop.Models;

namespace AYLink.Desktop.Services.Devices;

/// <summary>
/// 远程 Agent 设备 Provider
/// 负责将 AYLink.Agent 提供的设备接口适配到统一设备 Provider 抽象
/// </summary>
public sealed class AgentDeviceProvider(AgentServerRuntime runtime) : IDeviceProvider
{
    private readonly AgentServerRuntime _runtime = runtime;
    private List<DeviceDescriptor> _lastDevices = [];

    /// <summary>
    /// 当前 Provider 的唯一标识
    /// 直接使用对应服务器配置的 ID
    /// </summary>
    public string ProviderId => _runtime.Config.Id;

    /// <summary>
    /// 当前 Provider 的显示名称
    /// 直接使用对应服务器的显示名称
    /// </summary>
    public string DisplayName => _runtime.Config.DisplayName;

    /// <summary>
    /// 当前 Provider 的设备来源类型
    /// </summary>
    public DeviceSourceKind SourceKind => DeviceSourceKind.Agent;

    /// <summary>
    /// 刷新远程 Agent 设备列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>远程设备描述列表</returns>
    public async Task<IReadOnlyList<DeviceDescriptor>> RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var devices = await _runtime.Client.GetDevicesAsync(accessToken, cancellationToken);
        _lastDevices = devices.Select(Map).ToList();
        _runtime.TouchSuccess();
        return _lastDevices;
    }

    /// <summary>
    /// 向远程 Agent 提交新增设备请求
    /// </summary>
    /// <param name="request">设备接入请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新增后的远程设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> AddDeviceAsync(DeviceCreationRequest request, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var dto = await _runtime.Client.AddDeviceAsync(accessToken, new AgentCreateDeviceRequest
        {
            Serial = string.IsNullOrWhiteSpace(request.Serial) ? BuildSerial(request) : request.Serial.Trim(),
            Name = request.Name.Trim(),
            PairingPort = request.PairingPort,
            PairingCode = request.PairingCode.Trim()
        }, cancellationToken);

        _runtime.TouchSuccess();
        return Track(dto);
    }

    /// <summary>
    /// 请求远程 Agent 连接指定设备
    /// </summary>
    /// <param name="deviceId">统一远程设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接后的远程设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> ConnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (!TryParseRemoteId(deviceId, out var remoteId))
        {
            return null;
        }

        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var dto = await _runtime.Client.ConnectDeviceAsync(accessToken, remoteId, cancellationToken);
        _runtime.TouchSuccess();
        return Track(dto);
    }

    /// <summary>
    /// 请求远程 Agent 重命名指定设备
    /// </summary>
    /// <param name="deviceId">统一远程设备标识</param>
    /// <param name="newName">新的设备名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的远程设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> RenameDeviceAsync(string deviceId, string newName, CancellationToken cancellationToken = default)
    {
        if (!TryParseRemoteId(deviceId, out var remoteId))
        {
            return null;
        }

        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var dto = await _runtime.Client.RenameDeviceAsync(accessToken, remoteId, newName.Trim(), cancellationToken);
        _runtime.TouchSuccess();
        return Track(dto);
    }

    /// <summary>
    /// 远程 Provider 当前未实现主动断开能力
    /// </summary>
    /// <param name="deviceId">统一远程设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>始终返回 false</returns>
    public Task<bool> DisconnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// 检查远程设备是否在线
    /// 基于最近一次刷新得到的设备缓存判断
    /// </summary>
    /// <param name="deviceId">统一远程设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备是否在线</returns>
    public Task<bool> IsDeviceOnlineAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var matched = _lastDevices.FirstOrDefault(item => item.Id == deviceId);
        return Task.FromResult(matched?.IsConnected == true);
    }

    /// <summary>
    /// 远程 Provider 无法还原为本地 DeviceModel
    /// </summary>
    /// <param name="deviceId">统一远程设备标识</param>
    /// <param name="deviceModel">始终返回 null</param>
    /// <returns>始终返回 false</returns>
    public bool TryGetLocalDevice(string deviceId, out DeviceModel? deviceModel)
    {
        deviceModel = null;
        return false;
    }

    /// <summary>
    /// 将远程设备 DTO 写入本地缓存并返回统一设备描述
    /// </summary>
    /// <param name="dto">远程设备 DTO</param>
    /// <returns>统一设备描述</returns>
    private DeviceDescriptor Track(AgentDeviceDto dto)
    {
        var mapped = Map(dto);
        var existing = _lastDevices.FindIndex(item => item.Id == mapped.Id);
        if (existing >= 0)
        {
            _lastDevices[existing] = mapped;
        }
        else
        {
            _lastDevices.Add(mapped);
        }

        return mapped;
    }

    /// <summary>
    /// 将 Agent 返回的设备 DTO 转换为统一设备描述
    /// </summary>
    /// <param name="dto">远程设备 DTO</param>
    /// <returns>统一设备描述</returns>
    private DeviceDescriptor Map(AgentDeviceDto dto)
    {
        return new DeviceDescriptor
        {
            Id = $"{ProviderId}:{dto.Id}",
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            SourceKind = DeviceSourceKind.Agent,
            Name = dto.Name,
            Serial = dto.Serial,
            ConnectionType = InferConnectionType(dto.Serial, dto.IpAddress),
            Status = dto.Status,
            IsConnected = string.Equals(dto.Status, "online", StringComparison.OrdinalIgnoreCase),
            Capabilities = DeviceCapability.Connect | DeviceCapability.Rename,
            RemoteDeviceId = dto.Id
        };
    }

    /// <summary>
    /// 根据创建请求构建远程 Agent 需要的序列号格式
    /// </summary>
    /// <param name="request">设备接入请求</param>
    /// <returns>序列号字符串</returns>
    private static string BuildSerial(DeviceCreationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Serial))
        {
            return request.Serial.Trim();
        }

        var host = request.Host.Trim();
        var port = request.Port <= 0 ? 5555 : request.Port;
        return string.IsNullOrWhiteSpace(host) ? string.Empty : $"{host}:{port}";
    }

    /// <summary>
    /// 从统一远程设备标识中解析服务端设备整数 ID
    /// </summary>
    /// <param name="deviceId">统一远程设备标识</param>
    /// <param name="remoteId">解析得到的服务端设备 ID</param>
    /// <returns>是否解析成功</returns>
    private static bool TryParseRemoteId(string deviceId, out int remoteId)
    {
        remoteId = 0;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var index = deviceId.LastIndexOf(':');
        if (index < 0 || index == deviceId.Length - 1)
        {
            return false;
        }

        return int.TryParse(deviceId[(index + 1)..], out remoteId);
    }

    /// <summary>
    /// 推断远程设备的连接方式文本
    /// </summary>
    /// <param name="serial">设备序列号</param>
    /// <param name="ipAddress">设备 IP 地址</param>
    /// <returns>连接方式文本</returns>
    private static string InferConnectionType(string serial, string? ipAddress)
    {
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            return "WiFi";
        }

        if (serial.Contains('.') || serial.Contains(':'))
        {
            return "WiFi";
        }

        return "Remote";
    }
}
