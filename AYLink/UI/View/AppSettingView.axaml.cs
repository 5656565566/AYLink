using AdvancedSharpAdbClient;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AYLink.UI.Themes;
using AYLink.Utils;
using AYLink.Utils.Localization;
using SDL;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace AYLink.UI;

public partial class AppSettingView : UserControl
{
    private readonly LocalizationManager _localizationManager = LocalizationManager.Instance;
    private Color _currentAccentColor = Colors.BlueViolet;
    private readonly AudioPlayer _audioPlayer = AudioPlayer.Instance;
    private readonly ConfigManager _configManager = ConfigManager.Instance;
    public Config appConfig;

    private readonly Dictionary<ThemeMode, string> _themeMap = new()
    {
        { ThemeMode.Light, "亮色" },
        { ThemeMode.Dark, "暗色" },
        { ThemeMode.Default, "跟随系统" }
    };
    public class AudioDevice
    {
        public string? Name { get; set; }
        public SDL_AudioDeviceID InstanceID { get; set; }
    }

    public AppSettingView()
    {
        InitializeComponent();

        ThemeModeComboBox.ItemsSource = _themeMap;
        ThemeModeComboBox.SelectedItem = _themeMap.FirstOrDefault(x => x.Key == ThemeMode.Default);
        ThemeModeComboBox.SelectionChanged += ThemeModeComboBox_SelectionChanged;

        AccentColorPicker.ColorChanged += AccentColorPicker_ColorChanged;
        AccentColorPicker.Color = _currentAccentColor;

        appConfig = _configManager.LoadConfig<Config>("appConfig");

        var availableLanguages = _localizationManager.ListAvailableLanguages();
        LanguagesComboBox.ItemsSource = availableLanguages;
        var currentCultureName = _localizationManager.CurrentCulture.Name;
        LanguagesComboBox.SelectedItem = availableLanguages.FirstOrDefault(lang => lang.Culture == currentCultureName);
        LanguagesComboBox.SelectionChanged += LanguagesComboBox_SelectionChanged;

        var devicesTuple = AudioPlayer.GetPlaybackDevices();
        List<AudioDevice> devices = [.. devicesTuple.Select(d => new AudioDevice { Name = d.Name, InstanceID = d.InstanceID })];
        var systemDefaultDevice = new AudioDevice
        {
            Name = L.Tr("AppSettings_AudioDevice_SystemDefault"),
            InstanceID = 0
        };
        devices.Insert(0, systemDefaultDevice);
        AudioOutputDeviceComboBox.ItemsSource = devices;
        AudioOutputDeviceComboBox.SelectedItem = devices.FirstOrDefault(d => d.Name == appConfig.AudioOutputDevice);
        if (AudioOutputDeviceComboBox.SelectedItem == null)
        {
            AudioOutputDeviceComboBox.SelectedItem = systemDefaultDevice;
        }
        AudioOutputDeviceComboBox.SelectionChanged += OnAudioDeviceSelectionChanged;

        VolumeSlider.AddHandler(PointerReleasedEvent, OnVolumeSliderReleased, RoutingStrategies.Tunnel);
        VolumeSlider.Value = appConfig.GlobalVolume;
        float volume = (float)(VolumeSlider.Value / 100.0);
        _audioPlayer.SetGlobalVolume(volume);

        Loaded += AppSettingView_Loaded;

        scrcpyServer.Text = appConfig.ScrcpyServer;
        adb.Text = appConfig.Adb;
        FFmpegBin.Text = appConfig.FFmpegBin;
    }


    private async void OnAudioDeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
        {
            var selectedDevice = (AudioDevice)comboBox.SelectedItem;
            appConfig.AudioOutputDevice = selectedDevice.Name;

            if (selectedDevice.Name!.Contains("CABLE"))
            {
                await DialogHelper.MessageShowAsync(L.Tr("AppSettings_AudioOutputDevice_Title"), $"{L.Tr("AppSettings_AudioOutputDevice_Tip")}\n{L.Tr("AppSettings_AudioOutputDevice_Message")}");
            }

            if (selectedDevice.InstanceID == 0)
            {
                appConfig.AudioOutputDevice = null;
                _audioPlayer.ConfigureAudioDevice(null);
            }
            _audioPlayer.ConfigureAudioDevice(appConfig.AudioOutputDevice);
            _configManager.SaveConfig("appConfig", appConfig);
        }
    }

    private void OnVolumeSliderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider)
        {
            float volume = (float)(slider.Value / 100.0);

            _audioPlayer.SetGlobalVolume(volume);
            appConfig.GlobalVolume = (int)slider.Value;
            _configManager.SaveConfig("appConfig", appConfig);
        }
    }

    private void AppSettingView_Loaded(object? sender, RoutedEventArgs e)
    {
        if (AdbServer.Instance.GetStatus().IsRunning)
        {
            adbVersion.Text = AdbClient.Instance.GetAdbVersion().ToString();
        }        
    }

    private void LanguagesComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguagesComboBox.SelectedItem is LanguageInfo selectedLanguage)
        {
            _localizationManager.CurrentCulture = new CultureInfo(selectedLanguage.Culture);
            appConfig.Language = _localizationManager.CurrentCulture.Name;
            _configManager.SaveConfig("appConfig", appConfig);
        }
    }

    private void ThemeModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeModeComboBox.SelectedItem is KeyValuePair<ThemeMode, string> selectedPair)
        {
            ThemeMode selectedMode = selectedPair.Key;
            ThemeManager.SetTheme(selectedMode, _currentAccentColor);
        }
    }
    private void AccentColorPicker_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        _currentAccentColor = e.NewColor;

        if (ThemeModeComboBox.SelectedItem is KeyValuePair<ThemeMode, string> selectedPair)
        {
            ThemeMode selectedMode = selectedPair.Key;
            ThemeManager.SetTheme(selectedMode, _currentAccentColor);
        }
    }

    private async void SelectScrcpyServerBtn_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.Tr("AppSettings_ScrcpyServerTip"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Java Archive") { Patterns = ["*.jar", "*.*"] }
            ]
        });

        if (result.Count > 0)
        {
            appConfig.ScrcpyServer = result[0].Path.LocalPath;
            _configManager.SaveConfig("appConfig", appConfig);
            scrcpyServer.Text = appConfig.ScrcpyServer;
        }
    }

    private async void SelectAdbBtn_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var filter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { new FilePickerFileType("ADB Executable") { Patterns = ["adb.exe"] } }
            : null;

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.Tr("AppSettings_AdbTip"),
            AllowMultiple = false,
            FileTypeFilter = filter
        });

        if (result.Count > 0)
        {
            appConfig.Adb = result[0].Path.LocalPath;
            _configManager.SaveConfig("appConfig", appConfig);
            adb.Text = appConfig.Adb;
        }
    }

    private async void SelectFFmpegBtn_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.Tr("AppSettings_FFmpegBinTip"),
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            appConfig.FFmpegBin = result[0].Path.LocalPath;
            _configManager.SaveConfig("appConfig", appConfig);
            FFmpegBin.Text = appConfig.FFmpegBin;
        }
    }
}