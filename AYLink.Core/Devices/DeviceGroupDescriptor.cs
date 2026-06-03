namespace AYLink.Core.Devices;

/// <summary>
/// 统一设备分组描述模型
/// 用于在不同设备来源下表达设备所属的分组信息
/// </summary>
public sealed class DeviceGroupDescriptor
{
    /// <summary>
    /// 分组在所属 Provider 内的唯一标识
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 分组显示名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 分组描述
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 分组排序值
    /// </summary>
    public int SortOrder { get; init; }

    /// <summary>
    /// 是否为系统内置分组
    /// </summary>
    public bool IsSystem { get; init; }
}
