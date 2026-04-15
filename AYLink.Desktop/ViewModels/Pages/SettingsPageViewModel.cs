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

        _outputVolume = _appConfig.GlobalVolume;
        _scrcpyServer = _appConfig.ScrcpyServer;
        _adb = _appConfig.Adb;
        _fFmpegBin = _appConfig.FFmpegBin;
        _scrcpyVersion = _appConfig.ScrcpyVersion;

        // 配置中 null 对应 UI 上的"系统默认"翻译文本
        _selectedAudioOutputDevice = _appConfig.AudioOutputDevice
            ?? LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认");

        _enableAcrylic = _appConfig.EnableAcrylic;
        _enableBackgroundImage = _appConfig.EnableBackgroundImage;
        _isRandomBackground = _appConfig.BackgroundImageMode == "Random";
        _specificBackgroundImagePath = _appConfig.SpecificBackgroundImagePath;

        if (Color.TryParse(_appConfig.AccentColor, out var color))
        {
            _accentColor = color;
        }

        var themeMode = Enum.TryParse<ThemeMode>(_appConfig.ThemeMode, out var parsedMode) ? parsedMode : ThemeMode.Default;
        _selectedThemeMode = _themeMap[themeMode];

        // 初始化语言列表
        var availableLanguages = LocalizationManager.Instance.ListAvailableLanguages();
        foreach (var lang in availableLanguages) Languages.Add(lang);
        _selectedLanguage = availableLanguages.FirstOrDefault(l => l.Culture == _appConfig.Language) ?? availableLanguages.First();

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

    [ObservableProperty] private bool _isAutoStartEnabled;
    [ObservableProperty] private double _outputVolume;
    [ObservableProperty] private string? _selectedAudioOutputDevice;
    [ObservableProperty] private LanguageInfo _selectedLanguage;
    [ObservableProperty] private string _selectedThemeMode = LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统");
    [ObservableProperty] private Color _accentColor = Colors.BlueViolet;
    [ObservableProperty] private bool _enableAcrylic;
    [ObservableProperty] private bool _enableBackgroundImage;
    [ObservableProperty] private bool _isRandomBackground;
    [ObservableProperty] private string? _specificBackgroundImagePath;
    [ObservableProperty] private string _scrcpyVersion;
    [ObservableProperty] private string _scrcpyServer;
    [ObservableProperty] private string _adb;
    [ObservableProperty] private string _fFmpegBin;
    [ObservableProperty] private string _adbVersion = "Unknown";
    [ObservableProperty] private string _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";

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
        var isDefault = value == LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认");
        _appConfig.AudioOutputDevice = isDefault ? null : value;
        _audioPlayer.ConfigureAudioDevice(_appConfig.AudioOutputDevice);
        SaveConfig();
    }

    partial void OnSelectedLanguageChanged(LanguageInfo value)
    {
        if (value == null) return;
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
        ThemeManager.SetTheme(mode, _accentColor);
    }

    partial void OnAccentColorChanged(Color value)
    {
        _appConfig.AccentColor = value.ToString();
        SaveConfig();

        var mode = _themeMap.FirstOrDefault(x => x.Value == _selectedThemeMode).Key;
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
        FFmpeg.AutoGen.ffmpeg.RootPath = value; // TODO 要分离到 Core 库
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
    private void OpenUrl(string? url)
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
        DialogHelper.ShowToast(LocalizationManager.Instance.GetString(titleKey, defaultTitle), message, severity);
    }

    #endregion
}
