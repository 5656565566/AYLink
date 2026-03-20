using System;
using Avalonia;
using Avalonia.Input;
using AYLink.Core.Scrcpy;

namespace AYLink.Desktop.Services.Input;

/// <summary>
/// 控制输入处理器接口 负责将前端UI输入或外设输入转换为控制指令并发送
/// </summary>
public interface IInputProcessor : IDisposable
{
    /// <summary>
    /// 更新屏幕尺寸
    /// </summary>
    void UpdateScreenSize(Size size);

    /// <summary>
    /// 鼠标/触摸 按下
    /// </summary>
    void ProcessPointerPressed(PointerPressedEventArgs e, Point videoPoint, ScrcpyClient? client);

    /// <summary>
    /// 鼠标/触摸 移动
    /// </summary>
    void ProcessPointerMoved(PointerEventArgs e, Point videoPoint, ScrcpyClient? client);

    /// <summary>
    /// 鼠标/触摸 释放
    /// </summary>
    void ProcessPointerReleased(PointerReleasedEventArgs e, Point videoPoint, ScrcpyClient? client);

    /// <summary>
    /// 鼠标 滚轮
    /// </summary>
    void ProcessPointerWheelChanged(PointerWheelEventArgs e, Point videoPoint, ScrcpyClient? client);

    /// <summary>
    /// 焦点丢失
    /// </summary>
    void ProcessPointerCaptureLost(PointerCaptureLostEventArgs e, ScrcpyClient? client);

    /// <summary>
    /// 键盘按下
    /// </summary>
    void ProcessKeyDown(KeyEventArgs e, ScrcpyClient? client);

    /// <summary>
    /// 键盘抬起
    /// </summary>
    void ProcessKeyUp(KeyEventArgs e, ScrcpyClient? client);

    /// <summary>
    /// 清理并重置所有输入状态
    /// </summary>
    void ClearAll(ScrcpyClient? client);
}