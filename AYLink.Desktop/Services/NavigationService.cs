using System;
using System.Collections.Generic;

namespace AYLink.Desktop.Services;

/// <summary>
/// 导航服务实现 - 单例模式 支持任意位置调用
/// 
/// 使用方式：
///   // 无参数导航
///   NavigationService.Instance.NavigateTo("Home");
///   
///   // 类型安全的参数导航
///   NavigationService.Instance.NavigateTo("Screen", new ScreenNavigationArgs { ... });
///   
///   // 任意参数导航（方便快速迭代某个功能保留）
///   NavigationService.Instance.NavigateTo("File", someObject);
/// </summary>
public class NavigationService : INavigationService
{
    private static readonly Lazy<NavigationService> _instance = new(() => new NavigationService());

    /// <summary>
    /// 全局单例
    /// </summary>
    public static NavigationService Instance => _instance.Value;

    private readonly Stack<string> _history = new();

    public string? CurrentPageKey { get; private set; }

    public event Action<string>? Navigated;

    /// <summary>
    /// 页面切换并携带参数的事件
    /// </summary>
    public event Action<string, object?>? NavigatedWithParameter;

    public bool CanGoBack => _history.Count > 0;

    public void NavigateTo(string pageKey)
    {
        NavigateInternal(pageKey, null);
    }

    public void NavigateTo<TArgs>(string pageKey, TArgs args) where TArgs : NavigationArgs
    {
        NavigateInternal(pageKey, args);
    }

    public void NavigateTo(string pageKey, object? parameter)
    {
        NavigateInternal(pageKey, parameter);
    }

    public bool GoBack()
    {
        if (!CanGoBack)
            return false;

        var previousKey = _history.Pop();
        CurrentPageKey = previousKey;
        Navigated?.Invoke(previousKey);
        NavigatedWithParameter?.Invoke(previousKey, null);
        return true;
    }

    private void NavigateInternal(string pageKey, object? parameter)
    {
        if (CurrentPageKey == pageKey && parameter == null)
            return;

        if (CurrentPageKey != null)
        {
            _history.Push(CurrentPageKey);
        }

        CurrentPageKey = pageKey;
        Navigated?.Invoke(pageKey);
        NavigatedWithParameter?.Invoke(pageKey, parameter);
    }
}
