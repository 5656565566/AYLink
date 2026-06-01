namespace AYLink.Core.Devices;

/// <summary>
/// 统一设备描述模型
/// 作为首页列表与 Provider 聚合层之间的基础数据载体
/// </summary>
public sealed class DeviceDescriptor : IDeviceDescriptor
{
    /// <summary>
    /// 当前 Provider 内部唯一的设备标识
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 设备所属 Provider 的唯一标识
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// 设备所属 Provider 的显示名称
    /// </summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// 设备来源类型
    /// </summary>
    public DeviceSourceKind SourceKind { get; init; }

    /// <summary>
    /// 设备显示名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 设备序列号或地址标识
    /// </summary>
    public string Serial { get; init; } = string.Empty;

    /// <summary>
    /// 连接方式描述
    /// 例如 USB、WiFi、Remote 等
    /// </summary>
    public string ConnectionType { get; init; } = string.Empty;

    /// <summary>
    /// 设备状态文本
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 当前设备是否处于可用连接状态
    /// </summary>
    public bool IsConnected { get; init; }

    /// <summary>
    /// 当前设备开放的能力集合
    /// </summary>
    public DeviceCapability Capabilities { get; init; }

    /// <summary>
    /// 远程设备在 Agent 服务端中的整数 ID
    /// 本地设备通常为空
    /// </summary>
    public int? RemoteDeviceId { get; init; }
}
