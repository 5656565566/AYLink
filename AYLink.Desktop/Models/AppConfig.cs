using System.Globalization;

namespace AYLink.Desktop.Models;

public class AppConfig
{
    /// <summary>
    /// 应用配置在 <see cref="Services.ConfigManager"/> 中的存储名称
    /// </summary>
    public const string ConfigName = "appConfig";

    public string Language { get; set; } = CultureInfo.CurrentUICulture.Name;
    public string? AudioOutputDevice { get; set; } = null;
    public int GlobalVolume { get; set; } = 100;
    public bool MuteInBackground { get; set; } = false;
    public string ScrcpyServer { get; set; } = string.Empty;
    public string ScrcpyVersion { get; set; } = string.Empty;
    public string Adb { get; set; } = string.Empty;
    public string FFmpegBin { get; set; } = string.Empty;

    // 主题与外观设置
    public string ThemeMode { get; set; } = "Default";
    public string AccentColor { get; set; } = "#8A2BE2"; // 默认 BlueViolet
    public bool EnableAcrylic { get; set; } = false;
    public bool EnableBackgroundImage { get; set; } = false;
    public string BackgroundImageMode { get; set; } = "Random"; // "Random" 或 "Specific"
    public string? SpecificBackgroundImagePath { get; set; } = null;
}
