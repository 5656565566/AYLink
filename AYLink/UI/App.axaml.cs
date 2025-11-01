using AYLink.Utils.Localization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using System.Globalization;

namespace AYLink.UI;

public partial class App : Application
{
    private static readonly Color CardBackgroundFillColorDefaultBrush = Color.Parse("#4F3A3A3A");
    private readonly ConfigManager configManager = ConfigManager.Instance;
    public override void Initialize()
    {        
        AvaloniaXamlLoader.Load(this);

        Config appConfig = configManager.LoadConfig<Config>("appConfig");

        LocalizationManager.Instance.CurrentCulture = new CultureInfo(appConfig.Language);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();

        ActualThemeVariantChanged += (_, __) =>
        {
            if (ActualThemeVariant == ThemeVariant.Dark)
            {
                Resources["CardBackgroundFillColorDefaultBrush"] =
                    CardBackgroundFillColorDefaultBrush;
            }
            else
            {
                Resources.Remove("CardBackgroundFillColorDefaultBrush");
            }
        };

        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            Resources["CardBackgroundFillColorDefaultBrush"] =
                CardBackgroundFillColorDefaultBrush;
        }

    }
}