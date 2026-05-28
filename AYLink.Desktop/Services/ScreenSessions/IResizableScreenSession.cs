using Avalonia;

namespace AYLink.Desktop.Services.ScreenSessions;

/// <summary>
/// 提供调整显示尺寸能力的会话扩展接口
/// </summary>
public interface IResizableScreenSession
{
    /// <summary>
    /// 当前是否启用了弹性显示
    /// </summary>
    bool IsFlexDisplayEnabled { get; }

    /// <summary>
    /// 根据当前条件决定是否发送调整显示尺寸命令
    /// </summary>
    /// <param name="newSize">目标尺寸</param>
    /// <param name="hasReceivedFirstVideoFrame">是否已收到首帧视频</param>
    /// <param name="lastResizeRequestSize">上次发送的尺寸</param>
    /// <returns>本次是否实际发送了调整命令</returns>
    bool SendResizeDisplayIfNeeded(Size newSize, bool hasReceivedFirstVideoFrame, Size? lastResizeRequestSize);
}
