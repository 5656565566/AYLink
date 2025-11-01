using AYLink.Utils;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace AYLink.UI;

public partial class DeviceSettingView : UserControl
{
    public DeviceConfig? deviceConfig;
    public string? deviceName;
    public event Action? BackToHome;
    private readonly ConfigManager configManager = ConfigManager.Instance;
    public DeviceSettingView()
    {
        InitializeComponent();
    }
    public DeviceSettingView(string device)
    {
        InitializeComponent();
        deviceName = device;
        deviceConfig = configManager.LoadConfig<DeviceConfig>(HashHelper.ToMd5Hash(deviceName));
        DataContext = deviceConfig;

        ResetToDefaultsButton.Click += ResetToDefaultsButton_Click;
        BackToHomeButton.Click += BackToHomeButton_Click;
    }

    private void BackToHomeButton_Click(object? sender, RoutedEventArgs e)
    {
        BackToHome?.Invoke();
    }

    private void ResetToDefaultsButton_Click(object? sender, RoutedEventArgs e)
    {
        deviceConfig = new DeviceConfig();
        DataContext = deviceConfig;
    }
}