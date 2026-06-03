using System.Collections.Generic;

namespace AYLink.Desktop.Models;

/// <summary>
/// 设备分组配置
/// 以设备来源内的稳定设备标识保存设备与分组之间的多对多关系
/// </summary>
public sealed class DeviceGroupConfig
{
    /// <summary>
    /// 已定义的设备分组列表
    /// </summary>
    public List<DeviceGroupInfo> Groups { get; set; } = [];

    /// <summary>
    /// 设备分组映射表
    /// Key 为设备标识 Value 为设备所属的分组 ID 集合
    /// </summary>
    public Dictionary<string, List<string>> DeviceGroups { get; set; } = [];
}
