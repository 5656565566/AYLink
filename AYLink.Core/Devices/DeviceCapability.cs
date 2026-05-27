using System;

namespace AYLink.Core.Devices;

/// <summary>
/// 设备能力标记集合
/// 通过位标记描述当前设备在所属 Provider 下可执行的操作
/// </summary>
[Flags]
public enum DeviceCapability
{
    /// <summary>
    /// 不提供任何能力
    /// </summary>
    None = 0,

    /// <summary>
    /// 允许启动投屏
    /// </summary>
    Mirror = 1 << 0,

    /// <summary>
    /// 允许打开文件管理
    /// </summary>
    FileManager = 1 << 1,

    /// <summary>
    /// 允许打开应用管理
    /// </summary>
    AppManager = 1 << 2,

    /// <summary>
    /// 允许打开终端
    /// </summary>
    Shell = 1 << 3,

    /// <summary>
    /// 允许打开设备设置
    /// </summary>
    DeviceSettings = 1 << 4,

    /// <summary>
    /// 允许获取编码器列表
    /// </summary>
    ListEncoders = 1 << 5,

    /// <summary>
    /// 允许创建新显示
    /// </summary>
    NewDisplay = 1 << 6,

    /// <summary>
    /// 允许执行连接操作
    /// 常用于远程设备或网络设备接入流程
    /// </summary>
    Connect = 1 << 7,

    /// <summary>
    /// 允许执行重命名操作
    /// </summary>
    Rename = 1 << 8,

    /// <summary>
    /// 允许执行断开或移除连接操作
    /// </summary>
    Disconnect = 1 << 9
}
