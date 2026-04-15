using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.ViewModels;
using AYLink.Desktop.Views;
using System.Linq;

namespace AYLink.Desktop;

public partial class App : Application
{
    private readonly ConfigManager _configManager = ConfigManager.Instance;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // 加载配置项
        AppConfig appConfig = _configManager.LoadConfig<AppConfig>("appConfig");

        if (!string.IsNullOrWhiteSpace(appConfig.FFmpegBin))
        {
            FFmpeg.AutoGen.ffmpeg.RootPath = appConfig.FFmpegBin;  // TODO 要分离到 Core 库
        }

        // 根据配置切换语言
        if (!string.IsNullOrWhiteSpace(appConfig.Language))
        {
            var availableLanguages = LocalizationManager.Instance.ListAvailableLanguages();
            foreach (var lang in availableLanguages)
            {
                if (lang.Culture == appConfig.Language)
                {
                    LocalizationManager.Instance.CurrentCulture = new System.Globalization.CultureInfo(lang.Culture);
                    break;
                }
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 避免从 Avalonia 和 CommunityToolkit 同时进行重复验证
            DisableAvaloniaDataAnnotationValidation();

            // 初始化主题
            InitializeTheme();

            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

            // 应用退出时清理所有页面资源（Scrcpy 进程、音频流等）
            desktop.ShutdownRequested += (_, _) =>
            {
                vm.DisposeAllPages();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static void InitializeTheme()
        {
            var configManager = ConfigManager.Instance;
            var appConfig = configManager.LoadConfig<AppConfig>("appConfig");
    
            var themeMode = Themes.ThemeMode.Default;
            if (System.Enum.TryParse<Themes.ThemeMode>(appConfig.ThemeMode, out var parsedMode))
            {
                themeMode = parsedMode;
            }
    
            Avalonia.Media.Color? accentColor = null;
            if (Avalonia.Media.Color.TryParse(appConfig.AccentColor, out var color))
            {
                accentColor = color;
            }
    
            Themes.ThemeManager.SetTheme(themeMode, accentColor);
    }
}
