using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace AYLink.Utils;

/// <summary>
/// 窗口管理器，负责管理应用程序中所有窗口的注册、注销、查找和关闭
/// </summary>
internal sealed class WindowsManager
{
    #region 单例实现

    private static readonly Lazy<WindowsManager> _instance = new Lazy<WindowsManager>(() => new WindowsManager());
    public static WindowsManager Instance => _instance.Value;

    private WindowsManager()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += OnApplicationExit;
        }
    }

    #endregion

    private readonly List<Window> _managedWindows = []; // 被管理的窗口列表
    private readonly object _lock = new();

    // 存储最后已知的指针位置，用于按需查询
    private Point? _lastKnownPointerScreenPosition;

    /// <summary>
    /// 获取当前所有被管理窗口的只读列表。
    /// </summary>
    public IReadOnlyList<Window> ManagedWindows
    {
        get
        {
            lock (_lock)
            {
                return [.. _managedWindows]; // 返回副本以防止外部修改
            }
        }
    }

    /// <summary>
    /// 获取当前被管理窗口的数量。
    /// </summary>
    public int WindowCount
    {
        get
        {
            lock (_lock)
            {
                return _managedWindows.Count;
            }
        }
    }

    /// <summary>
    /// 创建指定类型的新窗口，注册它，并可选择性地配置它
    /// </summary>
    /// <typeparam name="TWindow">要创建的窗口类型</typeparam>
    /// <param name="configure">一个可选的Action，用于在注册前配置窗口</param>
    /// <returns>新创建并注册的窗口。</returns>
    public TWindow CreateWindow<TWindow>(Action<TWindow>? configure = null) where TWindow : Window, new()
    {
        var window = new TWindow();
        configure?.Invoke(window);
        RegisterWindow(window);
        return window;
    }

    /// <summary>
    /// 创建指定类型的新窗口，注册它，并显示它。
    /// </summary>
    /// <typeparam name="TWindow">要创建的窗口类型</typeparam>
    /// <param name="configure">一个可选的Action，用于在注册和显示前配置窗口</param>
    /// <returns>新创建、注册并显示的窗口</returns>
    public TWindow CreateAndShowWindow<TWindow>(Action<TWindow>? configure = null) where TWindow : Window, new()
    {
        var window = CreateWindow(configure);
        window.Show();
        return window;
    }

    /// <summary>
    /// 向管理器注册一个已存在的窗口。管理器将订阅其 Closed 事件
    /// </summary>
    /// <param name="window">要注册的窗口</param>
    public void RegisterWindow(Window window)
    {
        if (window == null) return;

        lock (_lock)
        {
            if (!_managedWindows.Contains(window))
            {
                _managedWindows.Add(window);
                window.Closed += OnWindowClosed;
                window.PointerMoved += OnWindowPointerMoved;
            }
        }
    }

    /// <summary>
    /// 从管理器注销一个窗口。管理器将取消订阅其事件
    /// </summary>
    /// <param name="window">要注销的窗口</param>
    public void UnregisterWindow(Window window)
    {
        if (window == null) return;

        lock (_lock)
        {
            if (_managedWindows.Remove(window))
            {
                window.Closed -= OnWindowClosed;
                window.PointerMoved -= OnWindowPointerMoved;
            }
        }
    }

    /// <summary>
    /// 关闭所有被管理的窗口，可以选择排除应用程序的主窗口
    /// </summary>
    /// <param name="excludeMainWindow">如果为 true，则不会关闭应用程序的主窗口</param>
    public void CloseAllWindows(bool excludeMainWindow = true)
    {
        List<Window> windowsToClose;

        lock (_lock)
        {
            windowsToClose = [.. _managedWindows];
        }

        foreach (var window in windowsToClose)
        {
            try
            {
                if (excludeMainWindow && IsMainWindow(window))
                {
                    continue;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (window.IsVisible)
                    {
                        window.Close();
                    }
                });
            }
            catch { }
        }
    }

    /// <summary>
    /// 根据最后已知的指针位置，获取指针当前所在位置最上面的窗口
    /// </summary>
    /// <returns>窗口，如果未找到则返回 null</returns>
    public Window? GetWindowUnderPointer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(GetWindowUnderPointer);

        if (_lastKnownPointerScreenPosition is not { } pt)
            return null;

        lock (_lock)
        {
            foreach (var w in _managedWindows)
            {
                if (w.IsVisible && IsPointInWindow(w, pt))
                    return w;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取指针当前所在位置的窗口和具体控件
    /// </summary>
    /// <returns>包含窗口和控件的元组，如果未找到则返回 (null, null)</returns>
    public (Window? Window, Control? Control) GetWindowAndControlUnderPointer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.Invoke(GetWindowAndControlUnderPointer);
        }

        var window = GetWindowUnderPointer();
        if (window == null) return (null, null);

        var control = GetControlUnderPointer(window);
        return (window, control);
    }

    /// <summary>
    /// 查找所有包含至少一个指定类型控件的被管理窗口
    /// </summary>
    /// <typeparam name="TControl">要搜索的控件类型</typeparam>
    /// <returns>包含指定控件类型的窗口列表</returns>
    public List<Window> FindWindowsWithControl<TControl>() where TControl : Control
    {
        lock (_lock)
        {
            return [.. _managedWindows.Where(window => window.FindDescendantOfType<TControl>() != null)];
        }
    }

    /// <summary>
    /// 查找所有标题包含指定字符串（不区分大小写）的被管理窗口
    /// </summary>
    /// <param name="titlePart">要在窗口标题中搜索的字符串</param>
    /// <returns>匹配标题部分的窗口列表</returns>
    public List<Window> FindWindowsByTitle(string titlePart)
    {
        lock (_lock)
        {
            return [.. _managedWindows.Where(window => window.Title?.Contains(titlePart, StringComparison.OrdinalIgnoreCase) == true)];
        }
    }

    /// <summary>
    /// 获取指针最后已知的屏幕位置。当指针在任何被管理窗口上移动时，此位置会持续更新
    /// </summary>
    /// <returns>最后已知的指针位置，可能为 null</returns>
    public Point? GetLastKnownPointerScreenPosition()
    {
        return _lastKnownPointerScreenPosition;
    }

    /// <summary>
    /// 处理被管理窗口的 Closed 事件以注销它
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            UnregisterWindow(window);
        }
    }

    /// <summary>
    /// 处理被管理窗口的 PointerMoved 事件以更新最后已知的指针位置
    /// </summary>
    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Window window)
        {
            // 获取相对于窗口的位置，然后转换为屏幕坐标
            var positionInWindow = e.GetPosition(window);
            _lastKnownPointerScreenPosition = window.PointToScreen(positionInWindow).ToPoint(1); // ToPoint(1) 用于与设备无关的像素 ToPoint(1)
        }
    }

    /// <summary>
    /// 处理应用程序退出事件，关闭所有被管理的窗口
    /// </summary>
    private void OnApplicationExit(object? sender, Avalonia.Controls.ApplicationLifetimes.ControlledApplicationLifetimeExitEventArgs e)
    {
        CloseAllWindows(false);
    }


    /// <summary>
    /// 判断一个窗口是否是应用程序的主窗口
    /// </summary>
    /// <param name="window">要检查的窗口</param>
    /// <returns>如果是主窗口则为 true，否则为 false</returns>
    private static bool IsMainWindow(Window window)
    {
        return Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
               desktop.MainWindow == window;
    }

    /// <summary>
    /// 检查给定的屏幕点是否落在特定窗口的边界内
    /// </summary>
    /// <param name="window">要检查的窗口</param>
    /// <param name="screenPoint">点的屏幕坐标</param>
    /// <returns>如果点在窗口边界内则为 true，否则为 false</returns>
    private static bool IsPointInWindow(Window window, Point screenPoint)
    {
        try
        {
            // 使用 window.Bounds，它是与设备无关的像素
            // 将 window.Position (PixelPoint) 转换为 Point 以构建 Rect
            var windowScreenPosition = window.PointToScreen(new Point(0, 0));
            var windowBounds = new Rect(windowScreenPosition.X, windowScreenPosition.Y, window.Bounds.Width, window.Bounds.Height);

            return windowBounds.Contains(screenPoint);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 在窗口上执行命中测试，以查找最后已知指针位置正下方的控件
    /// </summary>
    /// <param name="window">要执行命中测试的窗口</param>
    /// <returns>给定窗口内指针下方的控件，如果未找到则返回 null</returns>
    private Control? GetControlUnderPointer(Window window)
    {
        if (_lastKnownPointerScreenPosition == null) return null;

        try
        {
            var windowScreenPosition = window.PointToScreen(new Point(0, 0));
            var relativePoint = new Point(
                _lastKnownPointerScreenPosition.Value.X - windowScreenPosition.X,
                _lastKnownPointerScreenPosition.Value.Y - windowScreenPosition.Y
            );

            var hitTestResult = window.InputHitTest(relativePoint);
            return hitTestResult as Control;
        }
        catch
        {
            return null;
        }
    }
}