using AYLink.Utils.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;


namespace AYLink.UI.Themes;


public static class L
{
    /// <summary>
    /// 获取一个简单的翻译字符串。
    /// </summary>
    /// <param name="key">语言文件中的键。</param>
    /// <returns>翻译后的字符串。</returns>
    public static string Tr(string key)
    {
        return LocalizationManager.Instance[key];
    }

    /// <summary>
    /// 获取一个格式化的翻译字符串。
    /// </summary>
    /// <param name="key">语言文件中的键（应包含占位符，如 {0}）。</param>
    /// <param name="args">用于填充占位符的参数。</param>
    /// <returns>格式化并翻译后的字符串。</returns>
    public static string Tr(string key, params object[] args)
    {
        var template = LocalizationManager.Instance[key];
        return string.Format(template, args);
    }

    /// <summary>
    /// 将 Avalonia 控件的任何可绑定属性动态绑定到一个本地化键。
    /// </summary>
    /// <typeparam name="T">控件的类型，必须继承自 AvaloniaObject。</typeparam>
    /// <param name="control">要应用绑定的控件实例 (this)。</param>
    /// <param name="property">要绑定的目标属性 (例如 Button.ContentProperty)。</param>
    /// <param name="key">语言文件中的翻译键。</param>
    public static void BindLocalized<T>(this T control, AvaloniaProperty property, string key)
        where T : AvaloniaObject
    {
        var localizationManager = Locator.Localizer;

        var binding = new Binding
        {
            Source = localizationManager,
            Path = $"[{key}]",
            Mode = BindingMode.OneWay
        };

        control.Bind(property, binding);
    }

    /// <summary>
    /// 将 ContentControl (如 Button, Label) 的 Content 属性绑定到本地化键。
    /// </summary>
    public static void BindLocalizedContent(this ContentControl control, string key)
    {
        control.BindLocalized(ContentControl.ContentProperty, key);
    }

    /// <summary>
    /// 将 TextBlock 的 Text 属性绑定到本地化键。
    /// </summary>
    public static void BindLocalizedText(this TextBlock control, string key)
    {
        control.BindLocalized(TextBlock.TextProperty, key);
    }

    /// <summary>
    /// 将 HeaderedContentControl (如 GroupBox, Expander) 的 Header 属性绑定到本地化键。
    /// </summary>
    public static void BindLocalizedHeader(this HeaderedContentControl control, string key)
    {
        control.BindLocalized(HeaderedContentControl.HeaderProperty, key);
    }

    /// <summary>
    /// 将 TextBox 的 Watermark (水印) 属性绑定到本地化键。
    /// </summary>
    public static void BindLocalizedWatermark(this TextBox control, string key)
    {
        control.BindLocalized(TextBox.WatermarkProperty, key);
    }
}