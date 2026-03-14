using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AYLink.Desktop.Services;
using AYLink.Desktop.ViewModels.Pages;
using FluentAvalonia.UI.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// 主窗口 ViewModel - 通过 NavigationService 管理页面切换
/// 
/// 业务层可在任意位置通过以下方式切换页面：
///   NavigationService.Instance.NavigateTo("Screen");
///   NavigationService.Instance.NavigateTo("File", someParameter);
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    // 页面 ViewModel 注册表
    private readonly Dictionary<string, PageViewModelBase> _pages = new();

    // 导航服务
    private readonly NavigationService _navigationService = NavigationService.Instance;

    /// <summary>
    /// 当前显示的页面 ViewModel
    /// </summary>
    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    /// <summary>
    /// 当前选中的页面 Key，用于同步 NavigationView 选中状态
    /// </summary>
    [ObservableProperty]
    private string _selectedPageKey = "Home";

    /// <summary>
    /// 是否选中了 Settings
    /// </summary>
    [ObservableProperty]
    private bool _isSettingsSelected;

    /// <summary>
    /// 导航菜单项集合 - 供 View 层数据绑定
    /// </summary>
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new()
    {
        new("Home", "首页", Symbol.Home),
        new("File", "文件管理", Symbol.Folder),
        new("Screen", "投屏", Symbol.Play),
        new("App", "应用管理", Symbol.Repair),
        new("Shell", "终端", Symbol.Code),
    };

    public MainWindowViewModel()
    {
        // 注册所有页面
        RegisterPage(new HomePageViewModel());
        RegisterPage(new FilePageViewModel());
        RegisterPage(new ScreenPageViewModel());
        RegisterPage(new AppPageViewModel());
        RegisterPage(new ShellPageViewModel());
        RegisterPage(new SettingsPageViewModel());
        RegisterPage(new DeviceSettingViewModel());

        // 订阅导航服务事件
        _navigationService.NavigatedWithParameter += OnNavigated;

        // 初始导航到首页
        _navigationService.NavigateTo("Home");
    }

    /// <summary>
    /// 注册页面到注册表
    /// </summary>
    private void RegisterPage(PageViewModelBase pageViewModel)
    {
        _pages[pageViewModel.PageKey] = pageViewModel;
    }

    /// <summary>
    /// 导航服务触发的页面切换处理
    /// </summary>
    private void OnNavigated(string pageKey, object? parameter)
    {
        // 通知旧页面离开
        CurrentPage?.OnNavigatedFrom();

        // 查找目标页面
        if (_pages.TryGetValue(pageKey, out var targetPage))
        {
            targetPage.OnNavigatedTo(parameter);
            CurrentPage = targetPage;
        }

        // 同步选中状态
        IsSettingsSelected = pageKey == "Settings";
        SelectedPageKey = pageKey;

        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.PageKey == pageKey;
        }

        GoBackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 从 NavigationView 选中事件调用（由 View 的最小 code-behind 转发）
    /// </summary>
    public void OnNavItemSelected(string pageKey)
    {
        _navigationService.NavigateTo(pageKey);
    }

    /// <summary>
    /// 回退导航命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        _navigationService.GoBack();
    }

    private bool CanGoBack() => _navigationService.CanGoBack;
}
