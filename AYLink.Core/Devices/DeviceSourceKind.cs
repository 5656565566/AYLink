namespace AYLink.Core.Devices;

/// <summary>
/// 设备来源类型
/// 用于区分设备是来自本地运行环境还是远程 Agent 服务端
/// </summary>
public enum DeviceSourceKind
{
    /// <summary>
    /// 本地设备来源
    /// 由桌面端直接通过本地 ADB 访问
    /// </summary>
    Local = 0,

    /// <summary>
    /// 远程设备来源
    /// 由桌面端通过 AYLink.Agent 间接访问
    /// </summary>
    Agent = 1
}
