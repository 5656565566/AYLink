using System;
using Avalonia;

namespace AYLink.Desktop.Services.Input;

public enum PointerEventType
{
    Pressed,
    Moved,
    Released,
    WheelChanged,
    CaptureLost
}

public class PointerInput
{
    public PointerEventType EventType { get; set; }
    
    /// <summary>
    /// 事件发生的相对坐标 (0到真实屏幕宽高的范围内)
    /// </summary>
    public Point Position { get; set; }
    
    /// <summary>
    /// 唯一标识该触点/指针的 Hash Code
    /// </summary>
    public int PointerHash { get; set; }
    
    /// <summary>
    /// 滚轮事件的 X、Y 偏移
    /// </summary>
    public Vector WheelDelta { get; set; }
}

public enum KeyEventType
{
    Down,
    Up
}

public class KeyInput
{
    public KeyEventType EventType { get; set; }
    
    /// <summary>
    /// 按键的名称或标识 (这里暂时使用Avalonia.Input.Key.ToString()以便兼容)
    /// </summary>
    public string KeyName { get; set; } = string.Empty;
}
