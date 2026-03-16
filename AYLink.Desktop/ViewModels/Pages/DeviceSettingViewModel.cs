using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AYLink.Desktop.ViewModels.Pages;

public class DeviceSettingNavigationArgs : NavigationArgs
{
    public string DeviceSerial { get; init; } = string.Empty;
}

public partial class DeviceSettingViewModel : PageViewModelBase<DeviceSettingNavigationArgs>
{
    public override string PageKey => "DeviceSetting";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("DeviceSettingPage.Title", "设备设置");

    [ObservableProperty]
    private DeviceConfig _deviceConfig = new();

    private string _deviceSerial = string.Empty;

    protected override void OnNavigatedTo(DeviceSettingNavigationArgs args)
    {
        _deviceSerial = args.DeviceSerial;
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (!string.IsNullOrEmpty(_deviceSerial))
        {
            DeviceConfig = ConfigManager.Instance.LoadConfig<DeviceConfig>(HashHelper.ToMd5Hash(_deviceSerial));
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        if (!string.IsNullOrEmpty(_deviceSerial))
        {
            ConfigManager.Instance.SaveConfig(HashHelper.ToMd5Hash(_deviceSerial), DeviceConfig);
            var localizer = Services.Localization.LocalizationManager.Instance;
            DialogHelper.ShowToast(
                localizer.GetString("Dialog.Success", "成功"),
                localizer.GetString("DeviceSettingPage.SaveSuccess", "设备设置已保存"),
                FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        DeviceConfig = new DeviceConfig();
    }

    [RelayCommand]
    private void BackToHome()
    {
        SaveConfig();
        NavigationService.Instance.GoBack();
    }
}
