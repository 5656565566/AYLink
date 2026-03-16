using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using System;
using System.Linq;

namespace AYLink.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly NavigationService _navigationService = NavigationService.Instance;

    // 用于防止 ViewModel 导航触发 -> SelectionChanged -> 又触发导航的循环
    private bool _isSyncingSelection;

    /// <summary>
    /// 顶部拖拽区域的高度
    /// </summary>
    private const double TitleBarHeight = 40;

    public MainWindow()
    {
        InitializeComponent();

        // 注册全局背景图
        BackgroundImageManager.Instance.RegisterImageComponent(GlobalBackgroundImage);
        
        // 初始化背景图和亚克力状态
        var config = ConfigManager.Instance.LoadConfig<Models.AppConfig>("appConfig");
        UpdateAcrylicAndBackgroundState(config);

        // 订阅导航服务 同步 NavigationView 的选中状态
        _navigationService.Navigated += OnNavigationServiceNavigated;

        // 使用 Tunnel 策略在整个顶部区域实现窗口拖拽
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);

        // 将 SettingsItem 的绑定延迟到窗口加载完成后
        Loaded += (s, e) =>
        {
            NavView.SettingsItem.Content = LocalizationManager.Instance.GetString("MainWindow.NavSetting", "设置");
        };

        Closed += MainWindow_Closed;
    }

    public void UpdateAcrylicAndBackgroundState(Models.AppConfig config)
    {
        // 处理亚克力透明
        if (config.EnableAcrylic)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur];
            Background = Avalonia.Media.Brushes.Transparent;
            
            // 开启亚克力时 如果无背景图 提高透明度以透出桌面
            if (!config.EnableBackgroundImage)
            {
                BackgroundMask.Opacity = 0.4;
            }
            else
            {
                BackgroundMask.Opacity = 0.75;
            }
        }
        else
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            // 恢复默认背景色 使用 FluentAvalonia 的主题底色
            Background = Application.Current?.FindResource("SolidBackgroundFillColorBaseBrush") as Avalonia.Media.IBrush
                         ?? Avalonia.Media.Brushes.White;
                         
            // 关闭亚克力时 遮罩层完全不透明 恢复原有的纯色背景效果
            BackgroundMask.Opacity = 1.0;
        }

        // 处理背景图
        if (config.EnableBackgroundImage)
        {
            if (config.BackgroundImageMode == "Random")
            {
                BackgroundImageManager.Instance.SetRandomBackgroundImage();
            }
            else if (!string.IsNullOrEmpty(config.SpecificBackgroundImagePath))
            {
                BackgroundImageManager.Instance.SetBackgroundImage(config.SpecificBackgroundImagePath);
            }
            else
            {
                BackgroundImageManager.Instance.ClearBackgroundImage();
            }
        }
        else
        {
            BackgroundImageManager.Instance.ClearBackgroundImage();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _navigationService.Navigated -= OnNavigationServiceNavigated;

        // 清理所有投屏标签页 释放 Scrcpy 进程和音频流 防止进程泄露
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DisposeAllPages();
        }
    }

    /// <summary>
    /// 整个窗口的 PointerPressed 隧道处理
    /// 当点击位于顶部标题栏区域，且点击目标不是交互控件时 启动窗口拖拽
    /// </summary>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // 获取点击相对于窗口的位置
        var pos = e.GetPosition(this);
        if (pos.Y > TitleBarHeight)
            return;

        // 检查点击的原始控件是否为交互控件（按钮、输入框等）如果是则不拖拽
        if (e.Source is Visual sourceVisual && IsInteractiveControl(sourceVisual))
            return;

        e.Handled = true;
        BeginMoveDrag(e);
    }

    /// <summary>
    /// 判断一个 Visual 或其祖先是否是交互控件（按钮、输入框、滑块等）
    /// 这些控件不应被拖拽行为覆盖
    /// SettingsExpander 内部虽然包含 ToggleButton 等子控件 但整体视为非交互控件
    /// 允许拖拽穿透
    /// </summary>
    private static bool IsInteractiveControl(Visual visual)
    {
        // 先检查 SettingsExpander：如果点击目标位于 SettingsExpander 内部
        // 则视为非交互控件 允许拖拽穿透
        var check = visual;
        while (check != null)
        {
            if (check is SettingsExpander)
                return false;
            check = check.GetVisualParent() as Visual;
        }

        // 沿可视化树向上查找，看是否存在交互控件
        var current = visual;
        while (current != null)
        {
            if (current is Button ||
                current is RepeatButton ||
                current is ToggleButton ||
                current is TextBox ||
                current is ComboBox ||
                current is Slider ||
                current is ScrollBar ||
                current is CheckBox ||
                current is RadioButton ||
                current is NumericUpDown ||
                current is TabViewListView ||
                current is TabItem)
            {
                return true;
            }

            current = current.GetVisualParent() as Visual;
        }

        return false;
    }

    /// <summary>
    /// NavigationView 选中变化 -> 通知 ViewModel 导航
    /// </summary>
    private void NavView_OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;

        if (DataContext is MainWindowViewModel vm)
        {
            if (e.IsSettingsSelected)
            {
                vm.OnNavItemSelected("Settings");
            }
            else if (e.SelectedItem is NavigationViewItem selectedItem && selectedItem.Tag is string pageKey)
            {
                vm.OnNavItemSelected(pageKey);
            }
        }
    }

    /// <summary>
    /// 导航服务触发 -> 同步 NavigationView 的选中高亮
    /// </summary>
    private void OnNavigationServiceNavigated(string pageKey)
    {
        Dispatcher.UIThread.Post(() => SyncNavViewSelection(pageKey));
    }

    /// <summary>
    /// 将 NavigationView 的选中项同步到指定 pageKey
    /// </summary>
    private void SyncNavViewSelection(string pageKey)
    {
        _isSyncingSelection = true;
        try
        {
            if (pageKey == "Settings")
            {
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else
            {
                var targetItem = NavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(item => item.Tag is string tag && tag == pageKey);

                if (targetItem != null)
                {
                    NavView.SelectedItem = targetItem;
                }
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }
}
