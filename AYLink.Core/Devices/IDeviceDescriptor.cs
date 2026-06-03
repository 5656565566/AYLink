using System.Collections.Generic;

namespace AYLink.Core.Devices;

/// <summary>
/// 统一设备描述接口
/// 用于向上层暴露与具体实现无关的设备摘要信息
/// </summary>
public interface IDeviceDescriptor
{
    /// <summary>
    /// 当前 Provider 内部唯一的设备标识
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 设备所属 Provider 的唯一标识
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// 设备所属 Provider 的显示名称
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 设备来源类型
    /// </summary>
    DeviceSourceKind SourceKind { get; }

    /// <summary>
    /// 设备显示名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 设备序列号或地址标识
    /// </summary>
    string Serial { get; }

    /// <summary>
    /// 连接方式描述
    /// 例如 USB、WiFi、Remote 等
    /// </summary>
    string ConnectionType { get; }

    /// <summary>
    /// 设备状态文本
    /// 例如 online、offline 等
    /// </summary>
    string Status { get; }

    /// <summary>
    /// 当前设备是否处于可用连接状态
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 当前设备在此 Provider 下开放的能力集合
    /// </summary>
    DeviceCapability Capabilities { get; }

    /// <summary>
    /// 当前设备所属的分组集合
    /// </summary>
    IReadOnlyList<DeviceGroupDescriptor> Groups { get; }
}
