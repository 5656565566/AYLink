using CommunityToolkit.Mvvm.ComponentModel;
using AYLink.Desktop.Services;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// 页面 ViewModel 基类 - 所有页面 ViewModel 继承此类
/// 
/// 支持两种参数接收方式：
/// 重写 OnNavigatedTo(object?) - 通用方式
/// 继承 PageViewModelBase&lt;TArgs&gt; - 类型安全方式
/// </summary>
public abstract partial class PageViewModelBase : ViewModelBase
{
    /// <summary>
    /// 页面唯一标识 Key
    /// </summary>
    public abstract string PageKey { get; }

    /// <summary>
    /// 页面显示标题
    /// </summary>
    public abstract string Title { get; }

    /// <summary>
    /// 页面是否处于活跃状态
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// 页面被导航到时调用（可携带参数）
    /// </summary>
    public virtual void OnNavigatedTo(object? parameter = null)
    {
        IsActive = true;
    }

    /// <summary>
    /// 页面被导航离开时调用
    /// </summary>
    public virtual void OnNavigatedFrom()
    {
        IsActive = false;
    }
}

/// <summary>
/// 带类型安全参数的页面 ViewModel 基类
/// 
/// 使用示例：
/// <code>
/// public class ScreenPageViewModel : PageViewModelBase&lt;ScreenNavigationArgs&gt;
/// {
///     protected override void OnNavigatedTo(ScreenNavigationArgs args)
///     {
///         // args.DeviceSerial 等属性可直接使用 有编译时类型检查
///     }
/// }
/// </code>
/// </summary>
/// <typeparam name="TArgs">期望接收的导航参数类型</typeparam>
public abstract class PageViewModelBase<TArgs> : PageViewModelBase where TArgs : NavigationArgs
{
    public sealed override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);

        if (parameter is TArgs typedArgs)
        {
            OnNavigatedTo(typedArgs);
        }
        else
        {
            OnNavigatedToWithoutArgs();
        }
    }

    /// <summary>
    /// 携带类型安全参数的导航回调
    /// </summary>
    protected abstract void OnNavigatedTo(TArgs args);

    /// <summary>
    /// 无参数导航时的回调（如用户直接点击侧边栏）
    /// </summary>
    protected virtual void OnNavigatedToWithoutArgs() { }
}
