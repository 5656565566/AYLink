using System.Collections.Generic;

namespace AYLink.Desktop.Models;

/// <summary>
/// 本地设备别名配置
/// 以设备序列号为键保存用户自定义名称
/// </summary>
public sealed class LocalDeviceAliasConfig
{
    /// <summary>
    /// 本地设备别名映射表
    /// Key 为设备序列号 Value 为用户自定义名称
    /// </summary>
    public Dictionary<string, string> Aliases { get; set; } = [];
}
