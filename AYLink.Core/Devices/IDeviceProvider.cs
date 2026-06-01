using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Models;

namespace AYLink.Core.Devices;

/// <summary>
/// 统一设备 Provider 接口
/// 负责抽象本地设备与远程 Agent 设备的枚举、接入与基础管理行为
/// </summary>
public interface IDeviceProvider
{
    /// <summary>
    /// 当前 Provider 的唯一标识
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// 当前 Provider 的显示名称
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 当前 Provider 对应的设备来源类型
    /// </summary>
    DeviceSourceKind SourceKind { get; }

    /// <summary>
    /// 刷新并返回当前 Provider 可见的设备列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>统一设备描述集合</returns>
    Task<IReadOnlyList<DeviceDescriptor>> RefreshDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据给定请求新增设备或发起接入流程
    /// </summary>
    /// <param name="request">设备接入请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新增或接入成功后的设备描述；失败时返回 null</returns>
    Task<DeviceDescriptor?> AddDeviceAsync(DeviceCreationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按设备标识执行连接操作
    /// </summary>
    /// <param name="deviceId">当前 Provider 内部的设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接成功后的设备描述；失败时返回 null</returns>
    Task<DeviceDescriptor?> ConnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按设备标识执行重命名操作
    /// </summary>
    /// <param name="deviceId">当前 Provider 内部的设备标识</param>
    /// <param name="newName">新的设备显示名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的设备描述；失败时返回 null</returns>
    Task<DeviceDescriptor?> RenameDeviceAsync(string deviceId, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按设备标识执行断开或移除连接操作
    /// </summary>
    /// <param name="deviceId">当前 Provider 内部的设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作是否成功</returns>
    Task<bool> DisconnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查设备是否处于在线可用状态
    /// </summary>
    /// <param name="deviceId">当前 Provider 内部的设备标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备是否在线</returns>
    Task<bool> IsDeviceOnlineAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试将统一设备标识还原为本地 DeviceModel
    /// 仅本地 Provider 可用于复用现有本地深链路页面
    /// </summary>
    /// <param name="deviceId">当前 Provider 内部的设备标识</param>
    /// <param name="deviceModel">还原得到的本地设备模型</param>
    /// <returns>是否成功还原为本地设备模型</returns>
    bool TryGetLocalDevice(string deviceId, out DeviceModel? deviceModel);
}
