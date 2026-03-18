using CommunityToolkit.Mvvm.ComponentModel;
using AYLink.Desktop.Services;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// ViewModel 基类 - 提供导航服务的便捷访问
/// </summary>
public class ViewModelBase : ObservableValidator
{
    /// <summary>
    /// 导航服务 - 所有 ViewModel 可直接使用
    /// </summary>
    protected INavigationService Navigation => NavigationService.Instance;
}
