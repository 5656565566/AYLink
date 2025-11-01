using AYLink.UIModel;
using AYLink.Utils;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace AYLink.UI;

public enum SettingType
{
    APP,
    SCRCPY
}

public partial class SettingPage : UserControl
{
    private readonly AppSettingView _appSettingView = new();
    private DeviceSettingView? _deviceSettingView;
    private readonly ConfigManager configManager = ConfigManager.Instance;
    public event Action? BackToHome;

    public SettingPage()
    {
        InitializeComponent();

        Unloaded += SettingPage_Unloaded;
    }

    private void SettingPage_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (_deviceSettingView != null)
        {
            configManager.SaveConfig(HashHelper.ToMd5Hash(_deviceSettingView.deviceName!), _deviceSettingView.deviceConfig);
            _deviceSettingView = null;

            return; // 处理特殊设置页面
        }

        configManager.SaveConfig("appConfig", _appSettingView.appConfig);
    }

    public void GetSettingView(SettingType settingType, DeviceModel? deviceModel)
    {
        if (settingType == SettingType.APP)
        {
            Content = _appSettingView;
        }

        if (settingType == SettingType.SCRCPY && deviceModel != null)
        {
            _deviceSettingView = new DeviceSettingView(deviceModel.Serial);
            _deviceSettingView.BackToHome += BackToHome;
            Content = _deviceSettingView;
        }
    }
}