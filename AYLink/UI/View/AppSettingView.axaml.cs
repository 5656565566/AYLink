using AdvancedSharpAdbClient;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AYLink.UI.Themes;
using AYLink.Utils;
using AYLink.Utils.Localization;
using SDL;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

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
        { ThemeMode.Light, "明亮" },
        { ThemeMode.Dark, "暗黑" },
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
        AudioOutputDeviceComboBox.ItemsSource = devices;
        AudioOutputDeviceComboBox.SelectedItem = devices.FirstOrDefault(d => d.Name == appConfig.AudioOutputDevice);
        AudioOutputDeviceComboBox.SelectionChanged += OnAudioDeviceSelectionChanged;

        VolumeSlider.AddHandler(PointerReleasedEvent, OnVolumeSliderReleased, RoutingStrategies.Tunnel);
        VolumeSlider.Value = appConfig.GlobalVolume;
        float volume = (float)(VolumeSlider.Value / 100.0);
        _audioPlayer.SetGlobalVolume(volume);

        Loaded += AppSettingView_Loaded;
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

            _audioPlayer.ConfigureAudioDevice(selectedDevice.Name);
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

    /// <summary>
    /// 主题改变
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ThemeModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeModeComboBox.SelectedItem is KeyValuePair<ThemeMode, string> selectedPair)
        {
            ThemeMode selectedMode = selectedPair.Key;
            ThemeManager.SetTheme(selectedMode, _currentAccentColor);
        }
    }
    /// <summary>
    /// 当 ColorPicker 中的颜色发生变化
    /// </summary>
    private void AccentColorPicker_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        _currentAccentColor = e.NewColor;

        if (ThemeModeComboBox.SelectedItem is KeyValuePair<ThemeMode, string> selectedPair)
        {
            ThemeMode selectedMode = selectedPair.Key;
            ThemeManager.SetTheme(selectedMode, _currentAccentColor);
        }
    }
}