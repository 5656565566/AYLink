using AYLink.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// 标签页 ViewModel 基类 - 所有 Tab ViewModel 继承此类
/// 提供 Title、Device、StatusMessage、CloseTab 等通用功能
/// </summary>
public abstract partial class TabItemViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private DeviceModel? _device;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isClosable = true;

    /// <summary>
    /// 关闭请求事件 - 由 TabbedPageViewModelBase 订阅
    /// </summary>
    public event System.Action<TabItemViewModelBase>? OnCloseRequested;

    /// <summary>
    /// 关闭标签页（子类可 override 添加清理逻辑，但必须调用 base）
    /// </summary>
    [RelayCommand]
    protected virtual void CloseTab()
    {
        OnCloseRequested?.Invoke(this);
    }
}
