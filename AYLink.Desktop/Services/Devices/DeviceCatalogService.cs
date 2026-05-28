using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Devices;

namespace AYLink.Desktop.Services.Devices;

/// <summary>
/// 设备目录聚合服务
/// 负责统一汇总本地设备与多个远程 Agent 设备，并为首页提供统一访问入口
/// </summary>
public sealed class DeviceCatalogService
{
    /// <summary>
    /// 全局单例实例
    /// </summary>
    public static DeviceCatalogService Instance { get; } = new();

    private readonly LocalDeviceProvider _localProvider = new();
    private readonly LocalDeviceAliasService _localAliases = LocalDeviceAliasService.Instance;
    private readonly AgentSessionService _agentSessions = AgentSessionService.Instance;

    private DeviceCatalogService()
    {
        _agentSessions.ServersChanged += () => DevicesChanged?.Invoke();
    }

    /// <summary>
    /// 设备集合变化事件
    /// 当远程服务器集合变化时通知上层刷新设备列表
    /// </summary>
    public event Action? DevicesChanged;

    /// <summary>
    /// 获取当前已接入的远程 Agent 服务器运行时集合
    /// </summary>
    /// <returns>服务器运行时枚举</returns>
    public IEnumerable<AgentServerRuntime> GetAgentServers() => _agentSessions.Servers;

    /// <summary>
    /// 刷新所有来源的设备并返回聚合后的统一列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聚合后的统一设备描述列表</returns>
    public async Task<IReadOnlyList<DeviceDescriptor>> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<DeviceDescriptor>();
        all.AddRange((await _localProvider.RefreshDevicesAsync(cancellationToken)).Select(ApplyLocalAlias));

        foreach (var server in _agentSessions.Servers)
        {
            try
            {
                var provider = new AgentDeviceProvider(server);
                var remoteDevices = await provider.RefreshDevicesAsync(cancellationToken);
                all.AddRange(remoteDevices);
            }
            catch
            {
                // Keep partial results; the server page exposes connection errors.
            }
        }

        return all.OrderBy(item => item.SourceKind).ThenBy(item => item.ProviderName).ThenBy(item => item.Name).ToList();
    }

    /// <summary>
    /// 尝试根据统一设备标识取回本地 DeviceModel
    /// </summary>
    /// <param name="deviceId">统一设备标识</param>
    /// <param name="device">匹配到的本地设备模型</param>
    /// <returns>是否成功找到本地设备</returns>
    public bool TryGetLocalDevice(string deviceId, out AYLink.Core.Models.DeviceModel? device)
    {
        if (!_localProvider.TryGetLocalDevice(deviceId, out device) || device == null)
        {
            return false;
        }

        ApplyLocalAlias(device);
        return true;
    }

    /// <summary>
    /// 检查本地设备是否在线
    /// </summary>
    /// <param name="deviceId">统一设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备是否在线</returns>
    public Task<bool> IsLocalDeviceOnlineAsync(string deviceId, CancellationToken cancellationToken = default)
        => _localProvider.IsDeviceOnlineAsync(deviceId, cancellationToken);

    /// <summary>
    /// 新增本地设备
    /// </summary>
    /// <param name="request">设备接入请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新增后的本地设备描述；失败时返回 null</returns>
    public Task<DeviceDescriptor?> AddLocalDeviceAsync(DeviceCreationRequest request, CancellationToken cancellationToken = default)
        => AddLocalDeviceCoreAsync(request, cancellationToken);

    /// <summary>
    /// 断开本地设备
    /// </summary>
    /// <param name="deviceId">统一设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功进入断开流程</returns>
    public Task<bool> DisconnectLocalDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        => _localProvider.DisconnectDeviceAsync(deviceId, cancellationToken);

    /// <summary>
    /// 统一重命名设备
    /// 本地设备使用桌面端别名持久化 远程设备调用对应 Agent Provider
    /// </summary>
    /// <param name="descriptor">设备描述</param>
    /// <param name="newName">新的设备名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> RenameDeviceAsync(DeviceDescriptor descriptor, string newName, CancellationToken cancellationToken = default)
    {
        if (descriptor.SourceKind == DeviceSourceKind.Local)
        {
            return await RenameLocalDeviceAsync(descriptor, newName, cancellationToken);
        }

        var provider = CreateRemoteProvider(descriptor.ProviderId);
        return provider == null ? null : await provider.RenameDeviceAsync(descriptor.Id, newName, cancellationToken);
    }

    /// <summary>
    /// 连接远程设备
    /// </summary>
    /// <param name="descriptor">远程设备描述</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接后的设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> ConnectRemoteDeviceAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var provider = CreateRemoteProvider(descriptor.ProviderId);
        return provider == null ? null : await provider.ConnectDeviceAsync(descriptor.Id, cancellationToken);
    }

    /// <summary>
    /// 重命名远程设备
    /// </summary>
    /// <param name="descriptor">远程设备描述</param>
    /// <param name="newName">新的设备名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的设备描述；失败时返回 null</returns>
    public Task<DeviceDescriptor?> RenameRemoteDeviceAsync(DeviceDescriptor descriptor, string newName, CancellationToken cancellationToken = default)
        => RenameDeviceAsync(descriptor, newName, cancellationToken);

    /// <summary>
    /// 向指定远程服务器新增设备
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="request">设备接入请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新增后的远程设备描述；失败时返回 null</returns>
    public async Task<DeviceDescriptor?> AddRemoteDeviceAsync(string serverId, DeviceCreationRequest request, CancellationToken cancellationToken = default)
    {
        var provider = CreateRemoteProvider(serverId);
        return provider == null ? null : await provider.AddDeviceAsync(request, cancellationToken);
    }

    /// <summary>
    /// 根据 Provider ID 创建远程 Agent 设备 Provider
    /// </summary>
    /// <param name="providerId">Provider 唯一标识</param>
    /// <returns>匹配到的远程设备 Provider；不存在时返回 null</returns>
    private AgentDeviceProvider? CreateRemoteProvider(string providerId)
    {
        var server = _agentSessions.FindServer(providerId);
        return server == null ? null : new AgentDeviceProvider(server);
    }

    private async Task<DeviceDescriptor?> AddLocalDeviceCoreAsync(DeviceCreationRequest request, CancellationToken cancellationToken)
    {
        var added = await _localProvider.AddDeviceAsync(request, cancellationToken);
        if (added == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            _localAliases.SetAlias(added.Serial, request.Name);
            added = ApplyLocalAlias(added);
        }

        DevicesChanged?.Invoke();
        return added;
    }

    private async Task<DeviceDescriptor?> RenameLocalDeviceAsync(DeviceDescriptor descriptor, string newName, CancellationToken cancellationToken)
    {
        var trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return null;
        }

        _localAliases.SetAlias(descriptor.Serial, trimmedName);
        var renamed = await _localProvider.RenameDeviceAsync(descriptor.Id, trimmedName, cancellationToken);
        DevicesChanged?.Invoke();
        return renamed == null ? ApplyLocalAlias(descriptor) : ApplyLocalAlias(renamed);
    }

    private DeviceDescriptor ApplyLocalAlias(DeviceDescriptor descriptor)
    {
        if (descriptor.SourceKind != DeviceSourceKind.Local)
        {
            return descriptor;
        }

        var alias = _localAliases.GetAlias(descriptor.Serial);
        return string.IsNullOrWhiteSpace(alias)
            ? descriptor
            : new DeviceDescriptor
            {
                Id = descriptor.Id,
                ProviderId = descriptor.ProviderId,
                ProviderName = descriptor.ProviderName,
                SourceKind = descriptor.SourceKind,
                Name = alias,
                Serial = descriptor.Serial,
                ConnectionType = descriptor.ConnectionType,
                Status = descriptor.Status,
                IsConnected = descriptor.IsConnected,
                Capabilities = descriptor.Capabilities,
                RemoteDeviceId = descriptor.RemoteDeviceId
            };
    }

    private void ApplyLocalAlias(AYLink.Core.Models.DeviceModel device)
    {
        var alias = _localAliases.GetAlias(device.Serial);
        if (!string.IsNullOrWhiteSpace(alias))
        {
            device.Name = alias;
        }
    }
}
