using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace AYLink.UI.Themes;

/// <summary>
/// 定义应用程序支持的主题模式。
/// </summary>
public enum ThemeMode
{
    /// <summary>
    /// 浅色主题。
    /// </summary>
    Light,

    /// <summary>
    /// 深色主题。
    /// </summary>
    Dark,

    /// <summary>
    /// 跟随系统设置。
    /// </summary>
    Default
}

/// <summary>
/// 一个用于管理 FluentAvalonia 主题的工具类。
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// 设置应用程序的主题。
    /// </summary>
    /// <param name="mode">要设置的主题模式 (Light, Dark, 或 Default)。</param>
    /// <param name="customAccentColor">自定义主题色</param>
    public static void SetTheme(ThemeMode mode, Color? customAccentColor = null)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        if (app.Styles[0] is not FluentAvaloniaTheme faTheme)
        {
            return;
        }

        if (customAccentColor.HasValue)
        {
            faTheme.CustomAccentColor = customAccentColor.Value;
        }

        switch (mode)
        {
            case ThemeMode.Light:
                app.RequestedThemeVariant = ThemeVariant.Light;
                break;

            case ThemeMode.Dark:
                app.RequestedThemeVariant = ThemeVariant.Dark;
                break;

            case ThemeMode.Default:
                app.RequestedThemeVariant = null;
                break;
        }
    }
}