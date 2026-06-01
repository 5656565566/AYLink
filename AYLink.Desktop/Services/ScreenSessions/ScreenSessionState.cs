namespace AYLink.Desktop.Services.ScreenSessions;

/// <summary>
/// 表示投屏会话的当前状态
/// </summary>
public enum ScreenSessionState
{
    /// <summary>
    /// 当前尚未启动会话
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 当前正在建立会话连接
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// 当前会话已经建立完成
    /// </summary>
    Connected = 2,

    /// <summary>
    /// 当前会话已经关闭
    /// </summary>
    Closed = 3,

    /// <summary>
    /// 当前会话发生错误
    /// </summary>
    Faulted = 4
}
