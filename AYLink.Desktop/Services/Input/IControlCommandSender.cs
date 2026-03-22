using System;

namespace AYLink.Desktop.Services.Input;

/// <summary>
/// 控制指令发送器接口
/// 抽象出与设备通信的发送端，便于实现同步控制或脚本录制
/// </summary>
public interface IControlCommandSender
{
    /// <summary>
    /// 发送控制指令
    /// </summary>
    /// <param name="controlMessage">序列化后的指令数据</param>
    void SendCommand(byte[] controlMessage);
}
