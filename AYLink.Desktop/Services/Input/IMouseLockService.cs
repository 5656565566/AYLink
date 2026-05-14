using System;
using Avalonia;
using Avalonia.Controls;

namespace AYLink.Desktop.Services.Input;

/// <summary>
/// 管理窗口级鼠标锁定能力
/// </summary>
public interface IMouseLockService : IDisposable
{
    bool IsLocked { get; }

    /// <summary>
    /// 绑定当前用于交互的宿主控件
    /// </summary>
    void Attach(Control target);

    /// <summary>
    /// 解绑当前宿主控件
    /// </summary>
    void Detach();

    /// <summary>
    /// 设置是否需要锁定鼠标
    /// </summary>
    void SetLocked(bool locked);

    /// <summary>
    /// 将鼠标光标移动到宿主控件内指定位置
    /// </summary>
    void WarpCursorInTarget(Point point);

}
