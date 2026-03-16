using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;

namespace AYLink.Desktop.Services.Localization;

/// <summary>
/// 静态定位器 作为 XAML 资源提供 LocalizationManager 实例的桥梁
/// 需要在 App.axaml 中注册
/// <code>
/// &lt;local:Locator x:Key="Locator" /&gt;
/// </code>
/// </summary>
public class Locator
{
    public static LocalizationManager Localizer => LocalizationManager.Instance;
}

/// <summary>
/// XAML 标记扩展，用于绑定翻译文本 支持默认文本回退
/// 
/// 用法示例：
/// <code>
/// &lt;TextBlock Text="{local:Tr AppPage.Search, DefaultText='搜索'}" /&gt;
/// &lt;MenuItem Header="{local:Tr AppPage.CtxLaunch, DefaultText='启动应用'}" /&gt;
/// </code>
/// 
/// 当语言文件中找到对应键时 显示翻译文本
/// 找不到时 显示 DefaultText 作为回退
/// 两者都没有时 显示 #Key# 标记
/// </summary>

public class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// 翻译键 使用点分隔格式 如 "AppPage.CtxLaunch"
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 默认文本 当翻译文件中找不到对应键时使用
    /// </summary>
    public string? DefaultText { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 确保 Locator 资源已注册
        var staticResource = new StaticResourceExtension("Locator");
        if (staticResource.ProvideValue(serviceProvider) is not Locator)
        {
            throw new InvalidOperationException("Fatal Error: The 'Locator' resource was not found. " +
                "Please add <local:Locator x:Key=\"Locator\" /> to App.axaml Resources.");
        }

        var localizationManager = Locator.Localizer;

        // 注册默认文本（如果翻译文件中没有该键 使用 DefaultText）
        if (DefaultText != null && !localizationManager.Strings.ContainsKey(Key))
        {
            // 不修改原始字典，通过 FallbackValue 处理
        }

        var binding = new Binding
        {
            Source = localizationManager,
            Path = $"Strings[{Key}]",
            Mode = BindingMode.OneWay,
            FallbackValue = DefaultText ?? $"#{Key}#",
            TargetNullValue = DefaultText ?? $"#{Key}#",
        };

        return binding;
    }
}
