using Avalonia.Media;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class SettingsPageViewModel : PageViewModelBase
{
    public override string PageKey => "Settings";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.Title", "设置");

    private readonly ConfigManager _configManager = ConfigManager.Instance;
    private readonly AppConfig _appConfig;
    private readonly AudioPlayer _audioPlayer = AudioPlayer.Instance;

    private readonly Dictionary<ThemeMode, string> _themeMap = new()
    {
        { ThemeMode.Light, Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.ThemeLight", "亮色") },
        { ThemeMode.Dark, Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.ThemeDark", "暗色") },
        { ThemeMode.Default, Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统") }
    };

    public SettingsPageViewModel()
    {
        _appConfig = _configManager.LoadConfig<AppConfig>("appConfig");
        
        _outputVolume = _appConfig.GlobalVolume;
        _scrcpyServer = _appConfig.ScrcpyServer;
        _adb = _appConfig.Adb;
        _fFmpegBin = _appConfig.FFmpegBin;
        _scrcpyVersion = _appConfig.ScrcpyVersion;
        _selectedLanguage = _appConfig.Language;
        _selectedAudioOutputDevice = _appConfig.AudioOutputDevice;

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
        Languages.Add(_appConfig.Language);
        
        var devicesTuple = AudioPlayer.GetPlaybackDevices();
        AudioOutputDevices.Add(Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认"));
        foreach (var device in devicesTuple)
        {
            if (device.Name != null)
            {
                AudioOutputDevices.Add(device.Name);
            }
        }
    }

    public ObservableCollection<string> Languages { get; } = [];
    public ObservableCollection<string> AudioOutputDevices { get; } = [];
    public ObservableCollection<string> ThemeModes { get; } = [
        Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.ThemeLight", "亮色"),
        Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.ThemeDark", "暗色"),
        Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统")
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
        if (value == Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.SystemDefault", "系统默认"))
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
    private string _selectedLanguage;

    partial void OnSelectedLanguageChanged(string value)
    {
        _appConfig.Language = value;
        _configManager.SaveConfig("appConfig", _appConfig);
        // TODO 更改语言
    }

    [ObservableProperty]
    private string _selectedThemeMode = Services.Localization.LocalizationManager.Instance.GetString("SettingsPage.ThemeSystem", "跟随系统");

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
    private string _appVersion = "0.0.6-a";

    [RelayCommand]
    private void CheckForUpdates()
    {
        // TODO: 检查更新
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        // TODO: 恢复默认
    }
}
