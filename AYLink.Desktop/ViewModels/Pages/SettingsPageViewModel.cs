using Avalonia.Media;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class SettingsPageViewModel : PageViewModelBase
{
    public override string PageKey => "Settings";
    public override string Title => LocalizationManager.Instance.GetString("SettingsPage.Title", "设置");

    private readonly ConfigManager _configManager = ConfigManager.Instance;
    private readonly AudioPlayer _audioPlayer = AudioPlayer.Instance;
    private readonly UpdateService _updateService = new();

    private readonly Dictionary<ThemeMode, string> _themeMap = new()
    {
        { ThemeMode.Light, LocalizationManager.Instance.GetString("SettingsPage.ThemeLight", "亮色") },
        { ThemeMode.Dark, LocalizationManager.Instance.GetString("SettingsPage.ThemeDark", "暗色") },
        { ThemeMode.Default, LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统") }
    };

    private AppConfig _appConfig;

    public SettingsPageViewModel()
    {
        _appConfig = _configManager.LoadConfig<AppConfig>("appConfig");
        LoadAdbVersionAsync();
        OutputVolume = _appConfig.GlobalVolume;
        ScrcpyServer = _appConfig.ScrcpyServer;
        Adb = _appConfig.Adb;
        FFmpegBin = _appConfig.FFmpegBin;
        ScrcpyVersion = _appConfig.ScrcpyVersion;
        SelectedAudioOutputDevice = _appConfig.AudioOutputDevice
            ?? LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认");
        EnableAcrylic = _appConfig.EnableAcrylic;
        EnableBackgroundImage = _appConfig.EnableBackgroundImage;
        IsRandomBackground = _appConfig.BackgroundImageMode == "Random";
        SpecificBackgroundImagePath = _appConfig.SpecificBackgroundImagePath;

        if (Color.TryParse(_appConfig.AccentColor, out var color))
        {
            AccentColor = color;
        }

        var themeMode = Enum.TryParse<ThemeMode>(_appConfig.ThemeMode, out var parsedMode) ? parsedMode : ThemeMode.Default;
        SelectedThemeMode = _themeMap[themeMode];

        // 初始化语言列表
        var availableLanguages = LocalizationManager.Instance.ListAvailableLanguages();
        foreach (var lang in availableLanguages) Languages.Add(lang);
        SelectedLanguage = availableLanguages.FirstOrDefault(l => l.Culture == _appConfig.Language) ?? availableLanguages.First();

        // 初始化音频设备列表
        AudioOutputDevices.Add(LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认"));
        foreach (var (Name, _) in AudioPlayer.GetPlaybackDevices().Where(d => d.Name != null))
        {
            AudioOutputDevices.Add(Name);
        }
    }

    #region 集合

    public ObservableCollection<LanguageInfo> Languages { get; } = [];
    public ObservableCollection<string> AudioOutputDevices { get; } = [];
    public ObservableCollection<string> ThemeModes { get; } = [
        LocalizationManager.Instance.GetString("SettingsPage.ThemeLight", "亮色"),
        LocalizationManager.Instance.GetString("SettingsPage.ThemeDark", "暗色"),
        LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统")
    ];

    #endregion

    #region 观察属性

    [ObservableProperty]
    public partial bool IsAutoStartEnabled { get; set; }

    [ObservableProperty]
    public partial double OutputVolume { get; set; }

    [ObservableProperty]
    public partial string? SelectedAudioOutputDevice { get; set; }

    [ObservableProperty]
    public partial LanguageInfo SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial string SelectedThemeMode { get; set; } = LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统");

    [ObservableProperty]
    public partial Color AccentColor { get; set; } = Colors.BlueViolet;

    [ObservableProperty]
    public partial bool EnableAcrylic { get; set; }

    [ObservableProperty]
    public partial bool EnableBackgroundImage { get; set; }

    [ObservableProperty]
    public partial bool IsRandomBackground { get; set; }

    [ObservableProperty]
    public partial string? SpecificBackgroundImagePath { get; set; }

    [ObservableProperty]
    public partial string ScrcpyVersion { get; set; }

    [ObservableProperty]
    public partial string ScrcpyServer { get; set; }

    [ObservableProperty]
    public partial string Adb { get; set; }

    [ObservableProperty]
    public partial string FFmpegBin { get; set; }

    [ObservableProperty]
    public partial string AdbVersion { get; set; } = "Unknown";

    [ObservableProperty]
    public partial string AppVersion { get; set; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";

    #endregion

    #region 属性改变处理

    partial void OnOutputVolumeChanged(double value)
    {
        _appConfig.GlobalVolume = (int)value;
        SaveConfig();
        _audioPlayer.SetGlobalVolume((float)value);
    }

    partial void OnSelectedAudioOutputDeviceChanged(string? value)
    {
        var systemDefaultText = LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认");
        var normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var targetDevice = normalizedValue == systemDefaultText ? null : normalizedValue;
        
        if (targetDevice == _appConfig.AudioOutputDevice) return;

        _appConfig.AudioOutputDevice = targetDevice;
        _audioPlayer.ConfigureAudioDevice(_appConfig.AudioOutputDevice);
        SaveConfig();
    }

    partial void OnSelectedLanguageChanged(LanguageInfo value)
    {
        if (value == null) return;

        if (value.Culture == _appConfig.Language) return;

        _appConfig.Language = value.Culture;
        SaveConfig();

        LocalizationManager.Instance.CurrentCulture = new System.Globalization.CultureInfo(value.Culture);
        ShowWarning(LocalizationManager.Instance.GetString("SettingsPage.OnSelectedLanguageChanged", "部分文本需要重启后更新"));
    }

    partial void OnSelectedThemeModeChanged(string value)
    {
        var mode = _themeMap.FirstOrDefault(x => x.Value == value).Key;
        _appConfig.ThemeMode = mode.ToString();
        SaveConfig();
        ThemeManager.SetTheme(mode, AccentColor);
    }

    partial void OnAccentColorChanged(Color value)
    {
        _appConfig.AccentColor = value.ToString();
        SaveConfig();

        var mode = _themeMap.FirstOrDefault(x => x.Value == SelectedThemeMode).Key;
        ThemeManager.SetTheme(mode, value);
    }

    partial void OnEnableAcrylicChanged(bool value)
    {
        _appConfig.EnableAcrylic = value;
        if (!value) EnableBackgroundImage = false; // 强制关闭背景图
        SaveConfig();
        UpdateMainWindowState();
    }

    partial void OnEnableBackgroundImageChanged(bool value)
    {
        _appConfig.EnableBackgroundImage = value;
        if (value) EnableAcrylic = true; // 强制打开亚克力
        SaveConfig();
        UpdateMainWindowState();
    }

    partial void OnIsRandomBackgroundChanged(bool value)
    {
        _appConfig.BackgroundImageMode = value ? "Random" : "Specific";
        SaveConfig();
        UpdateMainWindowState();
    }

    partial void OnSpecificBackgroundImagePathChanged(string? value)
    {
        _appConfig.SpecificBackgroundImagePath = value;
        SaveConfig();
        UpdateMainWindowState();
    }

    partial void OnScrcpyVersionChanged(string value)
    {
        _appConfig.ScrcpyVersion = value;
        SaveConfig();
        ScrcpyService.Instance.Initialize(null, _appConfig.ScrcpyVersion);
    }

    partial void OnScrcpyServerChanged(string value)
    {
        _appConfig.ScrcpyServer = value;
        SaveConfig();
        ScrcpyService.Instance.Initialize(_appConfig.ScrcpyServer);
    }

    partial void OnAdbChanged(string value)
    {
        _appConfig.Adb = value;
        SaveConfig();
    }

    partial void OnFFmpegBinChanged(string value)
    {
        _appConfig.FFmpegBin = value;
        SaveConfig();
        Core.Utils.FFmpegConfig.SetRootPath(value);
    }

    #endregion

    #region 触发指令

    public async void LoadAdbVersionAsync()
    {
        try
        {
            var adbClient = new AdvancedSharpAdbClient.AdbClient();
            var version = await adbClient.GetAdbVersionAsync();
            AdbVersion = version.ToString();
        }
        catch
        {
            AdbVersion = "Unknown";
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var release = await _updateService.GetLatestReleaseAsync();
            if (string.IsNullOrWhiteSpace(release?.TagName))
            {
                ShowError(LocalizationManager.Instance.GetString("SettingsPage.CheckUpdateFailed", "检查更新失败"));
                return;
            }

            var latestVersionStr = release.TagName?.TrimStart('v', 'V');
            if (!Version.TryParse(latestVersionStr, out var latestVersion) ||
                !Version.TryParse(AppVersion, out var currentVersion))
            {
                ShowError("版本解析失败");
                return;
            }

            if (latestVersion <= currentVersion)
            {
                ShowInfo(LocalizationManager.Instance.GetString("SettingsPage.UpToDate", "当前已是最新版本"));
                return;
            }

            ShowSuccess($"{LocalizationManager.Instance.GetString("SettingsPage.UpdateAvailable", "发现新版本: ")} {release.TagName}");
            if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                ShowSuccess(LocalizationManager.Instance.GetString("SettingsPage.UpdateUrl", "请前往 GitHub 下载最新版本"));
            }
        }
        catch (Exception ex)
        {
            ShowError($"{LocalizationManager.Instance.GetString("SettingsPage.CheckUpdateFailed", "检查更新失败")}: {ex.Message}");
        }
    }

    [RelayCommand]
    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowError("链接地址无效");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"打开链接失败: {ex.Message}");
        }
    }
 
    [RelayCommand]
    private void ResetToDefaults()
    {
        _appConfig = new AppConfig();
        SaveConfig();
    }

    #endregion

    #region 辅助方法

    private void UpdateMainWindowState()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is Views.MainWindow mainWindow)
        {
            mainWindow.UpdateAcrylicAndBackgroundState(_appConfig);
        }
    }

    private void SaveConfig() => _configManager.SaveConfig("appConfig", _appConfig);

    private static void ShowInfo(string message) => ShowToast("Dialog.Tip", "提示", message, InfoBarSeverity.Informational);

    private static void ShowSuccess(string message) => ShowToast("Dialog.Tip", "提示", message, InfoBarSeverity.Success);

    private static void ShowWarning(string message) => ShowToast("Dialog.Tip", "提示", message, InfoBarSeverity.Warning);

    private static void ShowError(string message) => ShowToast("Dialog.Error", "错误", message, InfoBarSeverity.Error);

    private static void ShowToast(string titleKey, string defaultTitle, string message, InfoBarSeverity severity)
    {
        Services.Notifications.ToastManager.Instance.Show(LocalizationManager.Instance.GetString(titleKey, defaultTitle), message, severity);
    }

    #endregion
}
