using AYLink.Core.Scrcpy;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using NeoSmart.Unicode;
using Newtonsoft.Json.Linq;
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
    private readonly Dictionary<string, PageViewModelBase> _pages = [];

    // 导航服务
    private readonly NavigationService _navigationService = NavigationService.Instance;

    // 音频播放器
    private readonly AudioPlayer _audioPlayer = AudioPlayer.Instance;

    // 配置管理器
    private readonly ConfigManager _configManager = ConfigManager.Instance;

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
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } =
    [
        new("Home",LocalizationManager.Instance.GetString("MainWindow.NavHome", "首页"), Symbol.Home),
        new("File",LocalizationManager.Instance.GetString("MainWindow.NavFile", "文件管理"), Symbol.Folder),
        new("Screen",LocalizationManager.Instance.GetString("MainWindow.NavScreen", "投屏"), Symbol.Play),
        new("App",LocalizationManager.Instance.GetString("MainWindow.NavApp", "应用管理"), Symbol.Repair),
        new("Shell",LocalizationManager.Instance.GetString("MainWindow.NavShell", "终端"), Symbol.Code),
        new("TaskCenter", LocalizationManager.Instance.GetString("MainWindow.NavTaskCenter", "任务管理"), Symbol.Clock),
    ];

    public MainWindowViewModel()
    {
        // 注册所有页面
        RegisterPage(new HomePageViewModel());
        RegisterPage(new FilePageViewModel());
        RegisterPage(new ScreenPageViewModel());
        RegisterPage(new AppPageViewModel());
        RegisterPage(new ShellPageViewModel());
        RegisterPage(new TaskCenterPageViewModel());
        RegisterPage(new SettingsPageViewModel());
        RegisterPage(new DeviceSettingViewModel());

        // 订阅导航服务事件
        _navigationService.NavigatedWithParameter += OnNavigated;

        // 初始导航到首页
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _navigationService.NavigateTo("Home");
        });

        // 加载配置项
        AppConfig appConfig = _configManager.LoadConfig<AppConfig>("appConfig");

        // 初始化 Scrcpy 服务
        if (!string.IsNullOrWhiteSpace(appConfig.ScrcpyServer) &&
        !string.IsNullOrWhiteSpace(appConfig.ScrcpyVersion))
        {
            ScrcpyService.Instance.Initialize(appConfig.ScrcpyServer, appConfig.ScrcpyVersion);
        }
        else
        {
            ScrcpyService.Instance.Initialize();
        }

        // 初始化音频播放器
        _audioPlayer.ConfigureAudioDevice(appConfig.AudioOutputDevice);
        _audioPlayer.SetGlobalVolume(appConfig.GlobalVolume);
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
    /// 从 NavigationView 选中事件调用
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

    /// <summary>
    /// 释放所有实现了 IDisposable 的页面资源
    /// 在主窗口关闭时调用 确保 Scrcpy 进程等后台资源被正确清理
    /// </summary>
    public void DisposeAllPages()
    {
        foreach (var page in _pages.Values)
        {
            if (page is System.IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
