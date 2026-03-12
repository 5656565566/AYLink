using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Avalonia.Media;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class SettingsPageViewModel : PageViewModelBase
{
    public override string PageKey => "Settings";
    public override string Title => "设置";

    private readonly ConfigManager _configManager = ConfigManager.Instance;
    private AppConfig _appConfig;

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

        // 初始化一些默认选项
        Languages.Add(_appConfig.Language);
        if (_appConfig.AudioOutputDevice != null)
        {
            AudioOutputDevices.Add(_appConfig.AudioOutputDevice);
        }
        AudioOutputDevices.Add("系统默认");
    }

    public ObservableCollection<string> Languages { get; } = [];
    public ObservableCollection<string> AudioOutputDevices { get; } = [];
    public ObservableCollection<string> ThemeModes { get; } = ["亮色", "暗色", "跟随系统"];

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    [ObservableProperty]
    private double _outputVolume;

    partial void OnOutputVolumeChanged(double value)
    {
        _appConfig.GlobalVolume = (int)value;
        _configManager.SaveConfig("appConfig", _appConfig);
        // 留出位置给业务层代码：设置全局音量
        OnVolumeChangedBusinessLogic((float)(value / 100.0));
    }

    private void OnVolumeChangedBusinessLogic(float volume)
    {
        // TODO: 业务层代码，例如 _audioPlayer.SetGlobalVolume(volume);
    }

    [ObservableProperty]
    private string? _selectedAudioOutputDevice;

    partial void OnSelectedAudioOutputDeviceChanged(string? value)
    {
        _appConfig.AudioOutputDevice = value;
        _configManager.SaveConfig("appConfig", _appConfig);
        // 留出位置给业务层代码：配置音频设备
        OnAudioDeviceChangedBusinessLogic(value);
    }

    private void OnAudioDeviceChangedBusinessLogic(string? deviceName)
    {
        // TODO: 业务层代码，例如 _audioPlayer.ConfigureAudioDevice(deviceName);
    }

    [ObservableProperty]
    private string _selectedLanguage;

    partial void OnSelectedLanguageChanged(string value)
    {
        _appConfig.Language = value;
        _configManager.SaveConfig("appConfig", _appConfig);
        // 留出位置给业务层代码：更改语言
    }

    [ObservableProperty]
    private string _selectedThemeMode = "跟随系统";

    partial void OnSelectedThemeModeChanged(string value)
    {
        // 留出位置给业务层代码：更改主题
    }

    [ObservableProperty]
    private Color _accentColor = Colors.BlueViolet;

    partial void OnAccentColorChanged(Color value)
    {
        // 留出位置给业务层代码：更改强调色
    }

    [ObservableProperty]
    private string _scrcpyVersion;

    partial void OnScrcpyVersionChanged(string value)
    {
        _appConfig.ScrcpyVersion = value;
        _configManager.SaveConfig("appConfig", _appConfig);
    }

    [ObservableProperty]
    private string _scrcpyServer;

    partial void OnScrcpyServerChanged(string value)
    {
        _appConfig.ScrcpyServer = value;
        _configManager.SaveConfig("appConfig", _appConfig);
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
        // TODO: Check for updates
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        // TODO: Reset to defaults
    }
}
