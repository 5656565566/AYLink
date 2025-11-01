using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AYLink.Utils;

/// <summary>
/// 管理在 UI 线程上运行的后台循环事件。
/// 支持同步和异步任务，任务运行时不会阻塞 UI 线程。
/// 使用 Avalonia.Threading.DispatcherTimer。
/// </summary>
public sealed class BackgroundEventManager : IDisposable
{
    // 将内部存储委托统一为 Func<Task> 以支持异步操作
    private readonly ConcurrentDictionary<string, (DispatcherTimer timer, Func<Task> action)> _events = new();
    private readonly object _lock = new();
    private bool _isRunning = false;
    private static readonly LogHelper logHelper = LogHelper.Instance;

    public BackgroundEventManager() { }

    /// <summary>
    /// 添加一个在 UI 线程上循环执行的同步事件。
    /// </summary>
    /// <param name="eventName">事件的唯一名称</param>
    /// <param name="action">要执行的同步动作（将在 UI 线程上运行）</param>
    /// <param name="intervalMilliseconds">循环间隔（毫秒）</param>
    public void AddEvent(string eventName, Action action, int intervalMilliseconds)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        // 将同步的 Action 包装成一个返回已完成 Task 的异步委托
        Task asyncAction()
        {
            action();
            return Task.CompletedTask;
        }

        AddEventInternal(eventName, asyncAction, intervalMilliseconds);
    }

    /// <summary>
    /// (新增) 添加一个在 UI 线程上循环执行的异步事件。
    /// 任务本身会通过 await 执行，不会阻塞 UI 线程。
    /// </summary>
    /// <param name="eventName">事件的唯一名称</param>
    /// <param name="asyncAction">要执行的异步动作（将在 UI 线程上运行）</param>
    /// <param name="intervalMilliseconds">循环间隔（毫秒）</param>
    public void AddAsyncEvent(string eventName, Func<Task> asyncAction, int intervalMilliseconds)
    {
        if (asyncAction == null)
            throw new ArgumentNullException(nameof(asyncAction));

        AddEventInternal(eventName, asyncAction, intervalMilliseconds);
    }

    /// <summary>
    /// 内部核心方法，用于添加事件
    /// </summary>
    private void AddEventInternal(string eventName, Func<Task> action, int intervalMilliseconds)
    {
        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("事件名称不能为空。", nameof(eventName));

        if (intervalMilliseconds <= 0)
            throw new ArgumentException("时间间隔必须大于 0。", nameof(intervalMilliseconds));

        lock (_lock)
        {
            if (_events.ContainsKey(eventName))
            {
                // 如果已存在同名事件，先将其移除
                RemoveEvent(eventName);
            }

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMilliseconds)
            };

            // 将 Tick 事件处理器改为 async void，以便在其中使用 await
            timer.Tick += async (sender, e) =>
            {
                try
                {
                    // 只有在运行状态下才执行动作
                    if (_isRunning)
                    {
                        // 使用 await 执行任务，这不会阻塞UI线程
                        await action();
                    }
                }
                catch (Exception ex)
                {
                    logHelper.Error(ex);
                }
            };

            // 将新的 DispatcherTimer 和动作存入字典
            _events[eventName] = (timer, action);

            // 如果管理器当前处于运行状态，则立即启动新添加的计时器
            if (_isRunning)
            {
                timer.Start();
            }
        }
    }

    /// <summary>
    /// 移除指定事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    public void RemoveEvent(string eventName)
    {
        lock (_lock)
        {
            if (_events.TryRemove(eventName, out var eventData))
            {
                eventData.timer.Stop();
            }
        }
    }

    /// <summary>
    /// 清空所有事件
    /// </summary>
    public void ClearAllEvents()
    {
        lock (_lock)
        {
            foreach (var (timer, _) in _events.Values)
            {
                timer.Stop();
            }
            _events.Clear();
        }
    }

    /// <summary>
    /// 开始所有事件
    /// </summary>
    public void StartAllEvents()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                _isRunning = true;
                foreach (var eventData in _events.Values)
                {
                    eventData.timer.Start();
                }
            }
        }
    }

    /// <summary>
    /// 停止所有事件
    /// </summary>
    public void StopAllEvents()
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                _isRunning = false;
                foreach (var eventData in _events.Values)
                {
                    eventData.timer.Stop();
                }
            }
        }
    }

    /// <summary>
    /// 获取所有事件名称
    /// </summary>
    public string[] GetEventNames()
    {
        lock (_lock)
        {
            return _events.Keys.ToArray();
        }
    }

    /// <summary>
    /// 释放资源并清空所有事件
    /// </summary>
    public void Dispose()
    {
        ClearAllEvents();
    }
}