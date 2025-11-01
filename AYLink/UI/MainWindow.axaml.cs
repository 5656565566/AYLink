using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AYLink.ADB;
using AYLink.UI.Themes;
using AYLink.UIModel;
using AYLink.Utils;
using FluentAvalonia.UI.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.UI;

public partial class MainWindow : Window
{
    private readonly HomePage homePage = new();
    private readonly FilePage filePage = new();
    private readonly ScreenPage screenPage = new();
    private readonly SettingPage settingPage = new();
    private readonly ShellPage shellPage = new();
    private readonly AppPage appPage = new();

    private readonly AudioPlayer _player = AudioPlayer.Instance;
    private readonly AdbManager adbManager = AdbManager.Instance;
    private readonly WindowsManager windowsManager = WindowsManager.Instance;
    private readonly ConfigManager configManager = ConfigManager.Instance;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        NavView.SelectionChanged += NavView_OnSelectionChanged;
        homePage.RequestNavigate += OnNavigationRequested;
        settingPage.BackToHome += BackToHome; ;
        appPage.OnAppStart += OnAppStart;

        Config appConfig = configManager.LoadConfig<Config>("appConfig");

        windowsManager.RegisterWindow(this);
        _player.ConfigureAudioDevice(appConfig.AudioOutputDevice);
    }

    private void BackToHome()
    {
        ContentFrame.Content = homePage;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        screenPage.Dispose();
        _player.Dispose();
        shellPage.Dispose();

        adbManager.KillServer();
    }

    private async void MainWindow_Loaded(object? sender, EventArgs e)
    {
        NavView.SelectedItem = HomeNavItem;

        DialogHelper.GetProgressShow(L.Tr("MainWindow_Load"), L.Tr("MainWindow_Load_ADB"));
        DialogHelper.ShowProgress();

        if (!_player.IsAudioDeviceAvailable)
        {
            await DialogHelper.MessageShowAsync(L.Tr("MainWindow_Warning"), L.Tr("MainWindow_Warning_NoDevices"));
        }

        _ = Task.Run(() =>
        {
            if (!adbManager.TryStartAdbServer())
            {
                DialogHelper.CloseProgress();

                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(300); // 保证 ui 流畅性
                    await DialogHelper.MessageShowAsync(L.Tr("MainWindow_Warning"), L.Tr("MainWindow_Warning_ADB"));
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                });

            }
            else
            {
                DialogHelper.CloseProgress();
            }
        });
    }

    private void OnAppStart(DeviceModel deviceModel, string appName, string packageName)
    {
        string deviceName = $"{deviceModel.Name} - {appName}";
        screenPage.AddNewTab(deviceName, new ScreenView(deviceModel, packageName));
        NavigateTo("ScreenPage");
    }

    private void OnNavigationRequested(string pageTag, DeviceModel? deviceModel)
    {

        if (pageTag == "FilePage")
        {
            filePage.SetDevice(deviceModel!);
        }

        if (pageTag == "ScreenPage")
        {
            string? deviceName = deviceModel!.Name ?? deviceModel!.Serial;

            if (configManager.LoadConfig<DeviceConfig>(HashHelper.ToMd5Hash(deviceModel!.Serial)).Video == false)
            {
                screenPage.AddNewTab($"{deviceName} - no Video", new ScreenView(deviceModel, null));
            }
            else
            {
                screenPage.AddNewTab(deviceName, new ScreenView(deviceModel, null));
            }
        }

        if (pageTag == "AppPage")
        {
            appPage.SelectDevice(deviceModel!);
        }

        if (pageTag == "ShellPage")
        {
            shellPage.SelectDevice(deviceModel!);
        }

        if (pageTag == "SettingPage")
        {
            settingPage.GetSettingView(SettingType.SCRCPY, deviceModel);
        }

        NavigateTo(pageTag);
    }

    public void AddTab(TabViewItem tabViewItem)
    {
        screenPage.AddNewTab(tabViewItem);
    }

    public void ShowTip()
    {
        screenPage.ShowTip();
    }

    private void NavigateTo(string pageTag)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string tag && tag == pageTag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
        ContentFrame.Content = settingPage;
    }
    private void NavView_OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem selectedItem)
        {
            string? pageType = selectedItem.Tag as string;

            switch (pageType)
            {
                case "HomePage":
                    ContentFrame.Content = homePage;
                    break;

                case "FilePage":
                    ContentFrame.Content = filePage;
                    break;

                case "ScreenPage":
                    ContentFrame.Content = screenPage;
                    break;

                case "ShellPage":
                    ContentFrame.Content = shellPage;
                    break;

                case "AppPage":
                    ContentFrame.Content = appPage;
                    break;

                default:

                    settingPage.GetSettingView(SettingType.APP, null);
                    ContentFrame.Content = settingPage;
                    break;
            }
        }
    }
}