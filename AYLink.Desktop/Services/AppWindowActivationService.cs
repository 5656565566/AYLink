using Avalonia.Controls;
using Avalonia.Threading;
using AYLink.Desktop.Services.Audio;
using System;
using System.Collections.Generic;

namespace AYLink.Desktop.Services;

/// <summary>
/// 跟踪应用内窗口的激活状态 用于处理前后台相关的全局行为
/// </summary>
public sealed class AppWindowActivationService
{
    private static readonly Lazy<AppWindowActivationService> _instance = new(() => new AppWindowActivationService());
    public static AppWindowActivationService Instance => _instance.Value;

    private readonly HashSet<Window> _registeredWindows = [];
    private readonly HashSet<Window> _activeWindows = [];

    private AppWindowActivationService() { }

    public void Register(Window window)
    {
        if (!_registeredWindows.Add(window))
        {
            return;
        }

        window.Activated += Window_Activated;
        window.Deactivated += Window_Deactivated;
        window.Closed += Window_Closed;
    }

    public void Unregister(Window window)
    {
        if (!_registeredWindows.Remove(window))
        {
            return;
        }

        window.Activated -= Window_Activated;
        window.Deactivated -= Window_Deactivated;
        window.Closed -= Window_Closed;
        _activeWindows.Remove(window);
        ScheduleAudioStateRefresh();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            _activeWindows.Add(window);
            ScheduleAudioStateRefresh();
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            _activeWindows.Remove(window);
            ScheduleAudioStateRefresh();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            Unregister(window);
        }
    }

    private static void ScheduleAudioStateRefresh()
    {
        Dispatcher.UIThread.Post(
            () => AudioPlayer.Instance.SetAppActive(Instance._activeWindows.Count > 0),
            DispatcherPriority.Background);
    }
}
