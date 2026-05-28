using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Desktop.Services.Input;

namespace AYLink.Desktop.Services.ScreenSessions;

/// <summary>
/// 为页面层创建投屏会话与输入处理器
/// </summary>
public interface IScreenSessionFactory
{
    /// <summary>
    /// 创建本地投屏会话
    /// </summary>
    IScreenSession CreateLocalSession(DeviceModel device, string? appPackageName, string? appDisplayName);

    /// <summary>
    /// 创建远程投屏会话
    /// </summary>
    IScreenSession CreateRemoteSession(DeviceDescriptor remoteDevice, AgentServerRuntime remoteServer, string? appPackageName, string? appDisplayName);

    /// <summary>
    /// 根据当前设备选择输入处理器
    /// </summary>
    IInputProcessor CreateInputProcessor(DeviceModel? device);
}
