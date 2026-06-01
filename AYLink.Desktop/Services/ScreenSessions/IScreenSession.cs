using AYLink.Desktop.Services.Input;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.Services.ScreenSessions;

/// <summary>
/// 抽象一场投屏会话对上层暴露的最小能力集合
/// </summary>
public interface IScreenSession : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 当前会话状态
    /// </summary>
    ScreenSessionState State { get; }

    /// <summary>
    /// 当前会话状态变更时触发
    /// </summary>
    event Action<ScreenSessionState>? StateChanged;

    /// <summary>
    /// 当一帧视频被解码为 BGRA 后触发
    /// </summary>
    event Action<int, int, IntPtr, int>? VideoFrameDecoded;

    /// <summary>
    /// 当会话内部发生异常时触发
    /// </summary>
    event Action<Exception>? SessionError;

    /// <summary>
    /// 启动当前投屏会话
    /// </summary>
    /// <param name="inputProcessor">当前会话使用的输入处理器</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartAsync(IInputProcessor inputProcessor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止当前投屏会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送常规控制消息
    /// </summary>
    /// <param name="payload">控制消息负载</param>
    void SendControl(byte[] payload);

    /// <summary>
    /// 发送高频指针移动控制消息
    /// </summary>
    /// <param name="payload">控制消息负载</param>
    void SendPointerMove(byte[] payload);
}
