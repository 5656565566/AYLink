using AYLink.Core.Agent;
using AYLink.Core.Devices;
using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class DeviceSettingViewModel : PageViewModelBase<DeviceSettingNavigationArgs>
{
    public override string PageKey => "DeviceSetting";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("DeviceSettingPage.Title", "设备设置");

    [ObservableProperty]
    public partial string DeviceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DeviceConfig DeviceConfig { get; set; } = new();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Services.Localization.LocalizedRegularExpression(@"^\d*$", "DeviceSettings.InvalidResolution", "请输入合适的分辨率(长边) 例如 1920")]
    public partial string MaxSizeInput { get; set; } = string.Empty;

    partial void OnMaxSizeInputChanged(string value)
    {
        if (!GetErrors(nameof(MaxSizeInput)).Any())
        {
            DeviceConfig.MaxSize = string.IsNullOrEmpty(value) ? null : int.Parse(value);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Services.Localization.LocalizedRegularExpression(@"^\d*$", "DeviceSettings.InvalidBitRate", "请输入合适的码率 例如 8000000")]
    public partial string VideoBitRateInput { get; set; } = string.Empty;

    partial void OnVideoBitRateInputChanged(string value)
    {
        if (!GetErrors(nameof(VideoBitRateInput)).Any())
        {
            DeviceConfig.VideoBitRate = string.IsNullOrEmpty(value) ? null : int.Parse(value);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Services.Localization.LocalizedRegularExpression(@"^\d*\.?\d*$", "DeviceSettings.InvalidFps", "请输入合适的帧数 例如 60 或 59.94")]
    public partial string MaxFpsInput { get; set; } = string.Empty;

    partial void OnMaxFpsInputChanged(string value)
    {
        if (!GetErrors(nameof(MaxFpsInput)).Any())
        {
            DeviceConfig.MaxFps = string.IsNullOrEmpty(value) ? null : float.Parse(value, CultureInfo.InvariantCulture);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Services.Localization.LocalizedRegularExpression(@"^\d*$", "DeviceSettings.InvalidBitRate", "请输入合适的码率 例如 128000")]
    public partial string AudioBitRateInput { get; set; } = string.Empty;

    partial void OnAudioBitRateInputChanged(string value)
    {
        if (!GetErrors(nameof(AudioBitRateInput)).Any())
        {
            DeviceConfig.AudioBitRate = string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, CultureInfo.InvariantCulture);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Services.Localization.LocalizedRegularExpression(@"^-?\d*$", "DeviceSettings.InvalidScreenOffTimeout", "请输入合适的息屏超时时间 例如 -1 或 30000")]
    public partial string ScreenOffTimeoutInput { get; set; } = string.Empty;

    partial void OnScreenOffTimeoutInputChanged(string value)
    {
        if (!GetErrors(nameof(ScreenOffTimeoutInput)).Any())
        {
            DeviceConfig.ScreenOffTimeout = string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, CultureInfo.InvariantCulture);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Services.Localization.LocalizedRegularExpression(@"^$|^\d+\s*[xX]\s*\d+$", "DeviceSettings.InvalidCameraSize", "请输入合适的摄像头分辨率 例如 1920x1080")]
    public partial string CameraSizeInput { get; set; } = string.Empty;

    partial void OnCameraSizeInputChanged(string value)
    {
        if (!GetErrors(nameof(CameraSizeInput)).Any())
        {
            DeviceConfig.CameraSize = string.IsNullOrWhiteSpace(value) ? null : ParseCameraSize(value);
        }
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Services.Localization.LocalizedRegularExpression(@"^\d*$", "DeviceSettings.InvalidCameraFps", "请输入合适的摄像头帧率 例如 30")]
    public partial string CameraFpsInput { get; set; } = string.Empty;

    partial void OnCameraFpsInputChanged(string value)
    {
        if (!GetErrors(nameof(CameraFpsInput)).Any())
        {
            DeviceConfig.CameraFps = string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, CultureInfo.InvariantCulture);
        }
    }

    private string _deviceSerial = string.Empty;
    private DeviceDescriptor? _remoteDevice;
    private AgentServerRuntime? _remoteRuntime;

    private bool IsRemote => _remoteDevice != null && _remoteRuntime != null;

    protected override void OnNavigatedTo(DeviceSettingNavigationArgs args)
    {
        _deviceSerial = args.DeviceSerial;
        DeviceName = args.DeviceName;
        _remoteDevice = args.RemoteDevice;
        _remoteRuntime = string.IsNullOrWhiteSpace(args.ServerId) ? null : AgentSessionService.Instance.FindServer(args.ServerId);

        _ = LoadConfigAsync();
    }

    private async Task LoadConfigAsync()
    {
        try
        {
            DeviceConfig = IsRemote
                ? await LoadRemoteConfigAsync()
                : LoadLocalConfig();
            SyncInputFields();
        }
        catch (Exception ex)
        {
            ShowOperationError(ex, "加载设备设置失败");
        }
    }

    private DeviceConfig LoadLocalConfig()
    {
        if (string.IsNullOrEmpty(_deviceSerial))
        {
            return new DeviceConfig();
        }

        return ConfigManager.Instance.LoadConfig<DeviceConfig>(HashHelper.ToMd5Hash(_deviceSerial));
    }

    private async Task<DeviceConfig> LoadRemoteConfigAsync()
    {
        var runtime = _remoteRuntime ?? throw new InvalidOperationException("未找到远程服务器。");
        var remoteDeviceId = _remoteDevice?.RemoteDeviceId ?? throw new InvalidOperationException("当前远程设备缺少服务端 ID。");
        var accessToken = await runtime.EnsureAccessTokenAsync();
        var settings = await runtime.Client.GetDeviceSettingsAsync(accessToken, remoteDeviceId);
        runtime.TouchSuccess();
        return MapFromAgent(settings);
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
    private async Task SaveConfig()
    {
        await SaveConfigInternalAsync();
    }

    private async Task<bool> SaveConfigInternalAsync()
    {
        if (!TryApplyValidatedInputs())
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            NotificationService.Instance.ShowWarning(
                localizer.GetString("Dialog.Warning", "警告"),
                "请先修正输入错误后再保存");
            return false;
        }

        try
        {
            if (IsRemote)
            {
                await SaveRemoteConfigAsync();
            }
            else
            {
                SaveLocalConfig();
            }

            var localizer = Services.Localization.LocalizationManager.Instance;
            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                localizer.GetString("DeviceSettingPage.SaveSuccess", "设备设置已保存"));
            return true;
        }
        catch (Exception ex)
        {
            ShowOperationError(ex, "设备设置保存失败");
            return false;
        }
    }

    private void SaveLocalConfig()
    {
        if (string.IsNullOrEmpty(_deviceSerial))
        {
            throw new InvalidOperationException("当前设备序列号为空。");
        }

        ConfigManager.Instance.SaveConfig(HashHelper.ToMd5Hash(_deviceSerial), DeviceConfig);
    }

    private async Task SaveRemoteConfigAsync()
    {
        var runtime = _remoteRuntime ?? throw new InvalidOperationException("未找到远程服务器。");
        var remoteDeviceId = _remoteDevice?.RemoteDeviceId ?? throw new InvalidOperationException("当前远程设备缺少服务端 ID。");
        var accessToken = await runtime.EnsureAccessTokenAsync();
        var settings = await runtime.Client.SaveDeviceSettingsAsync(accessToken, remoteDeviceId, MapToAgent(DeviceConfig));
        runtime.TouchSuccess();
        DeviceConfig = MapFromAgent(settings);
        SyncInputFields();
    }

    [RelayCommand]
    private async Task ResetToDefaults()
    {
        try
        {
            if (IsRemote)
            {
                var runtime = _remoteRuntime ?? throw new InvalidOperationException("未找到远程服务器。");
                var remoteDeviceId = _remoteDevice?.RemoteDeviceId ?? throw new InvalidOperationException("当前远程设备缺少服务端 ID。");
                var accessToken = await runtime.EnsureAccessTokenAsync();
                var settings = await runtime.Client.ResetDeviceSettingsAsync(accessToken, remoteDeviceId);
                runtime.TouchSuccess();
                DeviceConfig = MapFromAgent(settings);
            }
            else
            {
                DeviceConfig = new DeviceConfig();
            }

            SyncInputFields();
        }
        catch (Exception ex)
        {
            ShowOperationError(ex, "恢复默认设置失败");
        }
    }

    [RelayCommand]
    private async Task BackToHome()
    {
        if (await SaveConfigInternalAsync())
        {
            NavigationService.Instance.GoBack();
        }
    }

    private void SyncInputFields()
    {
        MaxSizeInput = DeviceConfig.MaxSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        VideoBitRateInput = DeviceConfig.VideoBitRate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        MaxFpsInput = DeviceConfig.MaxFps?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        AudioBitRateInput = DeviceConfig.AudioBitRate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ScreenOffTimeoutInput = DeviceConfig.ScreenOffTimeout?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CameraSizeInput = FormatCameraSize(DeviceConfig.CameraSize);
        CameraFpsInput = DeviceConfig.CameraFps?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void ShowOperationError(Exception exception, string fallbackMessage)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var message = _remoteRuntime?.ResolveExceptionMessage(exception, fallbackMessage) ?? exception.Message;
        NotificationService.Instance.ShowError(
            localizer.GetString("Dialog.Error", "错误"),
            string.IsNullOrWhiteSpace(message) ? fallbackMessage : message);
    }

    private static DeviceConfig MapFromAgent(AgentDeviceSettingsDto settings)
    {
        return new DeviceConfig
        {
            Video = settings.Video,
            Audio = settings.Audio,
            Control = settings.Control,
            VideoCodec = settings.VideoCodec,
            MaxSize = settings.MaxSize,
            VideoBitRate = settings.VideoBitRate,
            MaxFps = settings.MaxFps.HasValue ? (float?)settings.MaxFps.Value : null,
            AudioCodec = settings.AudioCodec,
            AudioBitRate = settings.AudioBitRate,
            VideoSource = settings.VideoSource,
            AudioSource = settings.AudioSource,
            StayAwake = settings.StayAwake,
            ShowTouches = settings.ShowTouches,
            PowerOn = settings.PowerOn,
            PowerOffOnClose = settings.PowerOffOnClose,
            ScreenOffTimeout = settings.ScreenOffTimeout,
            HidKeyboard = settings.HidKeyboard,
            HidMouse = settings.HidMouse,
            CameraFacing = NullIfWhiteSpace(settings.CameraFacing),
            CameraId = NullIfWhiteSpace(settings.CameraId),
            CameraSize = ParseCameraSize(settings.CameraSize),
            CameraFps = int.TryParse(settings.CameraFps, out var fps) ? fps : null,
            CameraHighSpeed = settings.CameraHighSpeed,
            AudioDup = settings.AudioDup,
            VdDestroyContent = settings.VdDestroyContent,
            VdSystemDecorations = settings.VdSystemDecorations,
            NewDisplay = NullIfWhiteSpace(settings.NewDisplay),
            FlexDisplay = settings.FlexDisplay,
            VideoEncoder = NullIfWhiteSpace(settings.VideoEncoder),
            AudioEncoder = NullIfWhiteSpace(settings.AudioEncoder),
            CodecOptions = NullIfWhiteSpace(settings.CodecOptions)
        };
    }

    private static AgentDeviceSettingsDto MapToAgent(DeviceConfig config)
    {
        return new AgentDeviceSettingsDto
        {
            Video = config.Video,
            Audio = config.Audio,
            Control = config.Control,
            VideoCodec = config.VideoCodec,
            MaxSize = config.MaxSize,
            VideoBitRate = config.VideoBitRate,
            MaxFps = config.MaxFps,
            AudioCodec = config.AudioCodec,
            AudioBitRate = config.AudioBitRate,
            VideoSource = config.VideoSource,
            AudioSource = config.AudioSource,
            StayAwake = config.StayAwake,
            ShowTouches = config.ShowTouches,
            PowerOn = config.PowerOn,
            PowerOffOnClose = config.PowerOffOnClose,
            ScreenOffTimeout = config.ScreenOffTimeout,
            HidKeyboard = config.HidKeyboard,
            HidMouse = config.HidMouse,
            CameraFacing = config.CameraFacing ?? string.Empty,
            CameraId = config.CameraId ?? string.Empty,
            CameraSize = FormatCameraSize(config.CameraSize),
            CameraFps = config.CameraFps?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            CameraHighSpeed = config.CameraHighSpeed,
            AudioDup = config.AudioDup,
            VdDestroyContent = config.VdDestroyContent,
            VdSystemDecorations = config.VdSystemDecorations,
            NewDisplay = config.NewDisplay ?? string.Empty,
            FlexDisplay = config.FlexDisplay,
            VideoEncoder = config.VideoEncoder ?? string.Empty,
            AudioEncoder = config.AudioEncoder ?? string.Empty,
            CodecOptions = config.CodecOptions ?? string.Empty
        };
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Size? ParseCameraSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var segments = value.Trim().Split('x', 'X');
        if (segments.Length != 2 ||
            !int.TryParse(segments[0], out var width) ||
            !int.TryParse(segments[1], out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return null;
        }

        return new Size(width, height);
    }

    private static string FormatCameraSize(Size? size)
    {
        return size.HasValue && size.Value.Width > 0 && size.Value.Height > 0
            ? $"{size.Value.Width}x{size.Value.Height}"
            : string.Empty;
    }
}
