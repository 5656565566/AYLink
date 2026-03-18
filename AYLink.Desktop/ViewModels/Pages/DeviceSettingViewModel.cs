using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

public class DeviceSettingNavigationArgs : NavigationArgs
{
    public string DeviceSerial { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
}

public partial class DeviceSettingViewModel : PageViewModelBase<DeviceSettingNavigationArgs>
{
    public override string PageKey => "DeviceSetting";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("DeviceSettingPage.Title", "设备设置");

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private DeviceConfig _deviceConfig = new();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [RegularExpression(@"^\d*$", ErrorMessage = "请输入合适的分辨率(长边) 例如 1920")]
    private string _maxSizeInput = string.Empty;

    partial void OnMaxSizeInputChanged(string value)
    {
        if (!GetErrors(nameof(MaxSizeInput)).Any())
        {
            DeviceConfig.MaxSize = string.IsNullOrEmpty(value) ? null : int.Parse(value);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [RegularExpression(@"^\d*$", ErrorMessage = "请输入合适的码率 例如 8000000")]
    private string _videoBitRateInput = string.Empty;

    partial void OnVideoBitRateInputChanged(string value)
    {
        if (!GetErrors(nameof(VideoBitRateInput)).Any())
        {
            DeviceConfig.VideoBitRate = string.IsNullOrEmpty(value) ? null : int.Parse(value);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [RegularExpression(@"^\d*\.?\d*$", ErrorMessage = "请输入合适的帧数 例如 60 或 59.94")]
    private string _maxFpsInput = string.Empty;

    partial void OnMaxFpsInputChanged(string value)
    {
        if (!GetErrors(nameof(MaxFpsInput)).Any())
        {
            DeviceConfig.MaxFps = string.IsNullOrEmpty(value) ? null : float.Parse(value, CultureInfo.InvariantCulture);
        }
    }

    private string _deviceSerial = string.Empty;

    protected override void OnNavigatedTo(DeviceSettingNavigationArgs args)
    {
        _deviceSerial = args.DeviceSerial;
        DeviceName = args.DeviceName;
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (string.IsNullOrEmpty(_deviceSerial))
        {
            return;
        }

        DeviceConfig = ConfigManager.Instance.LoadConfig<DeviceConfig>(HashHelper.ToMd5Hash(_deviceSerial));
    }

    private bool TryApplyValidatedInputs()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            return false;
        }

        return true;
    }

    [RelayCommand]
    private void SaveConfig()
    {
        SaveConfigInternal();
    }

    private bool SaveConfigInternal()
    {
        if (string.IsNullOrEmpty(_deviceSerial))
        {
            return false;
        }

        if (!TryApplyValidatedInputs())
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            DialogHelper.ShowToast(
                localizer.GetString("Dialog.Warning", "提示"),
                "请先修正输入错误后再保存",
                InfoBarSeverity.Warning);
            return false;
        }

        ConfigManager.Instance.SaveConfig(HashHelper.ToMd5Hash(_deviceSerial), DeviceConfig);
        var successLocalizer = Services.Localization.LocalizationManager.Instance;
        DialogHelper.ShowToast(
            successLocalizer.GetString("Dialog.Success", "成功"),
            successLocalizer.GetString("DeviceSettingPage.SaveSuccess", "设备设置已保存"),
            InfoBarSeverity.Success);
        return true;
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        DeviceConfig = new DeviceConfig();
    }

    [RelayCommand]
    private void BackToHome()
{
    if (SaveConfigInternal())
    {
        NavigationService.Instance.GoBack();
    }
}
}
