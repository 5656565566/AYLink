namespace AYLink.Core.Devices;

/// <summary>
/// 创建设备或请求连接设备时使用的输入模型
/// 该模型同时兼容本地 ADB 接入与远程 Agent 代理接入流程
/// </summary>
public sealed class DeviceCreationRequest
{
    /// <summary>
    /// 目标设备主机地址
    /// 常用于通过网络调试接入设备
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// 目标设备端口
    /// 默认使用 ADB 网络调试常见端口 5555
    /// </summary>
    public int Port { get; init; } = 5555;

    /// <summary>
    /// 设备序列号
    /// 当调用方已知完整序列号时优先使用该字段
    /// </summary>
    public string Serial { get; init; } = string.Empty;

    /// <summary>
    /// 设备显示名称
    /// 如为空则由 Provider 按默认规则生成
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 无线配对端口
    /// 用于 Android 无线调试配对流程
    /// </summary>
    public int PairingPort { get; init; }

    /// <summary>
    /// 无线配对码
    /// </summary>
    public string PairingCode { get; init; } = string.Empty;
}
