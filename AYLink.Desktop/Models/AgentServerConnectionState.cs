namespace AYLink.Desktop.Models;

/// <summary>
/// Agent 服务器连接状态
/// 用于描述桌面端与远程 Agent 服务端之间的当前会话状态
/// </summary>
public enum AgentServerConnectionState
{
    /// <summary>
    /// 尚未建立连接或尚未尝试连接
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 正在连接或刷新登录态
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// 已成功连接并可正常访问服务端能力
    /// </summary>
    Connected = 2,

    /// <summary>
    /// 当前登录态无效，需要重新认证
    /// </summary>
    Unauthorized = 3,

    /// <summary>
    /// 连接或请求过程中发生错误
    /// </summary>
    Error = 4
}
