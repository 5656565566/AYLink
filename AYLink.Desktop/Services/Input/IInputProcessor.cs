using System;
using Avalonia;

namespace AYLink.Desktop.Services.Input;

/// <summary>
/// 控制输入处理器接口
/// 负责将前端抽象输入或外设输入转换为控制指令并发送
/// </summary>
public interface IInputProcessor : IDisposable
{
    /// <summary>
    /// 请求锁定/解锁鼠标光标 (用于支持FPS模拟视角等场景)
    /// </summary>
    event Action<bool>? CursorLockRequested;

    /// <summary>
    /// 设置指令发送器，处理器通过此接口发送生成的控制指令
    /// </summary>
    /// <param name="sender">指令发送器，可能是一个具体的Client，或者多控分配器</param>
    void SetSender(IControlCommandSender? sender);

    /// <summary>
    /// 更新屏幕尺寸
    /// </summary>
    void UpdateScreenSize(Size size);

    /// <summary>
    /// 处理指针事件 (鼠标/触摸)
    /// </summary>
    void ProcessPointer(PointerInput input);

    /// <summary>
    /// 处理键盘按键事件
    /// </summary>
    void ProcessKey(KeyInput input);

    /// <summary>
    /// 清理并重置所有输入状态，发送抬起所有按下的点和按键
    /// </summary>
    void ClearAll();
}