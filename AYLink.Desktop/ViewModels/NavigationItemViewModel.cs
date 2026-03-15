using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// 导航菜单项模型 - 用于 NavigationView 的数据绑定
/// </summary>
public partial class NavigationItemViewModel(string pageKey, string localizationKey, Symbol icon) : ObservableObject
{
    /// <summary>
    /// 页面唯一标识 Key，如 "Home"、"File"、"Screen"
    /// </summary>
    public string PageKey { get; } = pageKey;

    /// <summary>
    /// 本地化资源 Key，用于显示名称
    /// </summary>
    public string LocalizationKey { get; } = localizationKey;

    /// <summary>
    /// 图标
    /// </summary>
    public Symbol Icon { get; } = icon;

    [ObservableProperty]
    private bool _isSelected;
}
