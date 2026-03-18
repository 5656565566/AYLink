using System;

namespace AYLink.Desktop.Services;

/// <summary>
/// 导航服务接口 - 业务层可直接调用进行页面切换
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 当前页面的 Key
    /// </summary>
    string? CurrentPageKey { get; }

    /// <summary>
    /// 页面切换事件，参数为新页面的 Key
    /// </summary>
    event Action<string>? Navigated;

    /// <summary>
    /// 导航到指定页面（无参数）
    /// </summary>
    void NavigateTo(string pageKey);

    /// <summary>
    /// 导航到指定页面并传递类型安全的参数
    /// </summary>
    /// <typeparam name="TArgs">导航参数类型 必须继承 NavigationArgs</typeparam>
    void NavigateTo<TArgs>(string pageKey, TArgs args) where TArgs : NavigationArgs;

    /// <summary>
    /// 导航到指定页面并传递任意参数（向后兼容）
    /// </summary>
    void NavigateTo(string pageKey, object? parameter);

    /// <summary>
    /// 导航回上一页
    /// </summary>
    bool GoBack();

    /// <summary>
    /// 是否可以回退
    /// </summary>
    bool CanGoBack { get; }
}
