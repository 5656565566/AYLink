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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class SettingsPageViewModel : PageViewModelBase
{
    public override string PageKey => "Settings";
    public override string Title => LocalizationManager.Instance.GetString("SettingsPage.Title", "设置");

    private readonly ConfigManager _configManager = ConfigManager.Instance;
    private readonly AudioPlayer _audioPlayer = AudioPlayer.Instance;

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

        var themeMode = ThemeMode.Default;
        if (System.Enum.TryParse<ThemeMode>(_appConfig.ThemeMode, out var parsedMode))
        {
            themeMode = parsedMode;
        }
        _selectedThemeMode = _themeMap[themeMode];

        // 初始化一些默认选项
        var availableLanguages = LocalizationManager.Instance.ListAvailableLanguages();
        foreach (var lang in availableLanguages)
        {
            Languages.Add(lang);
        }
        _selectedLanguage = availableLanguages.FirstOrDefault(l => l.Culture == _appConfig.Language) ?? availableLanguages.First();
        
        var devicesTuple = AudioPlayer.GetPlaybackDevices();
        AudioOutputDevices.Add(LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认"));
        foreach (var (Name, InstanceID) in devicesTuple)
        {
            if (Name != null)
            {
                AudioOutputDevices.Add(Name);
            }
        }
    }

    public ObservableCollection<LanguageInfo> Languages { get; } = [];
    public ObservableCollection<string> AudioOutputDevices { get; } = [];
    public ObservableCollection<string> ThemeModes { get; } = [
        LocalizationManager.Instance.GetString("SettingsPage.ThemeLight", "亮色"),
        LocalizationManager.Instance.GetString("SettingsPage.ThemeDark", "暗色"),
        LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统")
    ];

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    [ObservableProperty]
    private double _outputVolume;

    partial void OnOutputVolumeChanged(double value)
    {
        _appConfig.GlobalVolume = (int)value;
        _configManager.SaveConfig("appConfig", _appConfig);
        _audioPlayer.SetGlobalVolume((float)value);
    }

    [ObservableProperty]
    private string? _selectedAudioOutputDevice;

    partial void OnSelectedAudioOutputDeviceChanged(string? value)
    {
        if (value == LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认"))
        {
            _appConfig.AudioOutputDevice = null;
            _audioPlayer.ConfigureAudioDevice(null);
        }
        else
        {
            _appConfig.AudioOutputDevice = value;
            _audioPlayer.ConfigureAudioDevice(value);
        }
        _configManager.SaveConfig("appConfig", _appConfig);
    }

    [ObservableProperty]
    private LanguageInfo _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageInfo value)
    {
        if (value == null) return;
        _appConfig.Language = value.Culture;
        _configManager.SaveConfig("appConfig", _appConfig);

        LocalizationManager.Instance.CurrentCulture = new System.Globalization.CultureInfo(value.Culture);

        DialogHelper.ShowToast(
            LocalizationManager.Instance.GetString("Dialog.Tip", "提示"),
            LocalizationManager.Instance.GetString("SettingsPage.OnSelectedLanguageChanged", "部分文本需要重启后更新"),
            InfoBarSeverity.Warning);
    }

    [ObservableProperty]
    private string _selectedThemeMode = LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统");

    partial void OnSelectedThemeModeChanged(string value)
    {
        var mode = _themeMap.FirstOrDefault(x => x.Value == value).Key;
        _appConfig.ThemeMode = mode.ToString();
        _configManager.SaveConfig("appConfig", _appConfig);
        
        ThemeManager.SetTheme(mode, _accentColor);
    }

    [ObservableProperty]
    private Color _accentColor = Colors.BlueViolet;

    partial void OnAccentColorChanged(Color value)
    {
        _appConfig.AccentColor = value.ToString();
        _configManager.SaveConfig("appConfig", _appConfig);

        var mode = _themeMap.FirstOrDefault(x => x.Value == _selectedThemeMode).Key;
        ThemeManager.SetTheme(mode, value);
    }

    [ObservableProperty]
    private bool _enableAcrylic;

    partial void OnEnableAcrylicChanged(bool value)
    {
        _appConfig.EnableAcrylic = value;
        if (!value)
        {
            // 如果关闭亚克力 强制关闭背景图
            EnableBackgroundImage = false;
        }
        _configManager.SaveConfig("appConfig", _appConfig);
        
        OnAcrylicStateChangedBusinessLogic(value);
    }

    private void OnAcrylicStateChangedBusinessLogic(bool isEnabled)
    {
        UpdateMainWindowState();
    }

    [ObservableProperty]
    private bool _enableBackgroundImage;

    partial void OnEnableBackgroundImageChanged(bool value)
    {
        _appConfig.EnableBackgroundImage = value;
        if (value)
        {
            // 如果打开背景图 强制打开亚克力
            EnableAcrylic = true;
        }
        _configManager.SaveConfig("appConfig", _appConfig);

        OnBackgroundImageStateChangedBusinessLogic(value);
    }

    private void OnBackgroundImageStateChangedBusinessLogic(bool isEnabled)
    {
        UpdateMainWindowState();
    }

    [ObservableProperty]
    private bool _isRandomBackground;

    partial void OnIsRandomBackgroundChanged(bool value)
    {
        _appConfig.BackgroundImageMode = value ? "Random" : "Specific";
        _configManager.SaveConfig("appConfig", _appConfig);

        UpdateMainWindowState();
    }

    [ObservableProperty]
    private string? _specificBackgroundImagePath;

    partial void OnSpecificBackgroundImagePathChanged(string? value)
    {
        _appConfig.SpecificBackgroundImagePath = value;
        _configManager.SaveConfig("appConfig", _appConfig);

        UpdateMainWindowState();
    }

    private void UpdateMainWindowState()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is Views.MainWindow mainWindow)
            {
                mainWindow.UpdateAcrylicAndBackgroundState(_appConfig);
            }
        }
    }

    [ObservableProperty]
    private string _scrcpyVersion;

    partial void OnScrcpyVersionChanged(string value)
    {
        _appConfig.ScrcpyVersion = value;
        _configManager.SaveConfig("appConfig", _appConfig);
        ScrcpyService.Instance.Initialize(null, _appConfig.ScrcpyVersion);
    }

    [ObservableProperty]
    private string _scrcpyServer;

    partial void OnScrcpyServerChanged(string value)
    {
        _appConfig.ScrcpyServer = value;
        _configManager.SaveConfig("appConfig", _appConfig);
        ScrcpyService.Instance.Initialize(_appConfig.ScrcpyServer);
    }

    [ObservableProperty]
    private string _adb;

    partial void OnAdbChanged(string value)
    {
        _appConfig.Adb = value;
        _configManager.SaveConfig("appConfig", _appConfig);
    }

    [ObservableProperty]
    private string _fFmpegBin;

    partial void OnFFmpegBinChanged(string value)
    {
        _appConfig.FFmpegBin = value;
        _configManager.SaveConfig("appConfig", _appConfig);
    }

    [ObservableProperty]
    private string _adbVersion = "Unknown";

    [ObservableProperty]
    private string _appVersion = "0.0.6";

    [RelayCommand]
    private void CheckForUpdates()
    {
        // TODO: 检查更新
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _appConfig = new AppConfig();
        _configManager.SaveConfig("appConfig", _appConfig);
    }
}
