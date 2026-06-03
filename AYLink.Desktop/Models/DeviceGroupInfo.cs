namespace AYLink.Desktop.Models;

/// <summary>
/// 设备分组配置项
/// </summary>
public sealed class DeviceGroupInfo
{
    /// <summary>
    /// 分组唯一标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 分组显示名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 分组描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 分组排序值
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否为系统内置分组
    /// </summary>
    public bool IsSystem { get; set; }
}
