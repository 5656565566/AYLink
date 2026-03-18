using System;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Generic;

namespace AYLink.Desktop.Views;

/// <summary>
/// 全局脱离标签页窗口管理器
/// 跟踪所有 DetachedTabWindow 实例，支持跨窗口的标签页合并
/// </summary>
public sealed class DetachedTabWindowManager
{
    private static readonly Lazy<DetachedTabWindowManager> _instance = new(() => new DetachedTabWindowManager());
    public static DetachedTabWindowManager Instance => _instance.Value;

    private readonly List<DetachedTabWindow> _windows = [];

    private DetachedTabWindowManager() { }

    /// <summary>
    /// 注册一个脱离窗口实例
    /// </summary>
    public void Register(DetachedTabWindow window)
    {
        if (!_windows.Contains(window))
        {
            _windows.Add(window);
        }
    }

    /// <summary>
    /// 注销一个脱离窗口实例
    /// </summary>
    public void Unregister(DetachedTabWindow window)
    {
        _windows.Remove(window);
    }

    /// <summary>
    /// 获取所有已注册的脱离窗口实例
    /// </summary>
    public IReadOnlyList<DetachedTabWindow> Windows => _windows.AsReadOnly();

    /// <summary>
    /// 根据屏幕坐标查找鼠标下方的 DetachedTabWindow（排除指定窗口）
    /// </summary>
    public DetachedTabWindow? FindWindowUnderPoint(PixelPoint screenPoint, DetachedTabWindow? exclude = null)
    {
        foreach (var window in _windows)
        {
            if (window == exclude) continue;
            if (!window.IsVisible) continue;

            try
            {
                // 检查屏幕坐标是否在窗口范围内
                var windowBounds = new PixelRect(window.Position, PixelSize.FromSize(window.Bounds.Size, window.RenderScaling));
                if (windowBounds.Contains(screenPoint))
                {
                    return window;
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }
}
