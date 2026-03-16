using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AYLink.Desktop.Services.Localization;

/// <summary>
/// 语言信息记录
/// </summary>
public record LanguageInfo(string Culture, string DisplayName);

/// <summary>
/// 本地化管理器 - 支持嵌套 JSON 语言文件和默认文本回退。
/// 
/// 语言文件格式（嵌套 JSON）：
/// {
///   "LanguageName": "中文（简体）",
///   "AppPage": {
///     "CtxLaunch": "启动应用",
///     "Search": "搜索"
///   }
/// }
/// 
/// 键名使用点分隔访问：AppPage.CtxLaunch
/// </summary>
public class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private readonly string _languageFolderPath;
    private const string LanguageNameKey = "LanguageName";
    private const string DefaultFallbackCulture = "zh-CN";

    /// <summary>
    /// 扁平化的翻译字典 键为点分隔路径（如 "AppPage.CtxLaunch"）
    /// </summary>
    public Dictionary<string, string> Strings { get; private set; } = [];

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 通过键获取翻译文本 找不到时返回 #key# 标记
    /// </summary>
    public string this[string key] => Strings.TryGetValue(key, out var value) ? value : $"#{key}#";

    /// <summary>
    /// 通过键获取翻译文本 支持默认文本回退
    /// </summary>
    /// <param name="key">翻译键</param>
    /// <param name="defaultText">默认文本</param>
    /// <returns></returns>
    public string GetString(string key, string? defaultText = null)
    {
        if (Strings.TryGetValue(key, out var value))
            return value;
        return defaultText ?? $"#{key}#";
    }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (Equals(_currentCulture, value)) return;
            _currentCulture = value;
            LoadLanguage(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Strings)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    private LocalizationManager()
    {
        _languageFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Language");

        if (!Directory.Exists(_languageFolderPath))
        {
            Directory.CreateDirectory(_languageFolderPath);
        }

        LoadLanguage(CurrentCulture);
    }

    /// <summary>
    /// 扫描语言文件夹 列出所有可用的语言
    /// </summary>
    /// <returns></returns>
    public List<LanguageInfo> ListAvailableLanguages()
    {
        var languages = new List<LanguageInfo>();
        var files = Directory.GetFiles(_languageFolderPath, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var jObj = JObject.Parse(json);
                var culture = Path.GetFileNameWithoutExtension(file);
                var displayName = jObj[LanguageNameKey]?.Value<string>() ?? culture;
                languages.Add(new LanguageInfo(culture, displayName));
            }
            catch { }
        }

        return [.. languages.OrderBy(l => l.DisplayName)];
    }

    private void LoadLanguage(CultureInfo culture)
    {
        var filePath = Path.Combine(_languageFolderPath, $"{culture.Name}.json");
        Dictionary<string, string> newStrings;

        try
        {
            if (!File.Exists(filePath))
            {
                if (culture.Name != DefaultFallbackCulture)
                    LoadLanguage(new CultureInfo(DefaultFallbackCulture));
                return;
            }

            var json = File.ReadAllText(filePath);
            var jObj = JObject.Parse(json);

            // 扁平化嵌套 JSON 为点分隔键
            newStrings = FlattenJson(jObj);

            // 移除语言名称键（它不是翻译文本）
            newStrings.Remove(LanguageNameKey);
        }
        catch
        {
            newStrings = [];
        }

        Strings = newStrings;
    }

    /// <summary>
    /// 将嵌套 JSON 对象递归扁平化为点分隔键的字典
    /// 例如：{"AppPage": {"Search": "搜索"}} → {"AppPage.Search": "搜索"}
    /// 同时兼容旧的扁平格式（顶层字符串值直接保留）
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="prefix"></param>
    /// <returns></returns>
    private static Dictionary<string, string> FlattenJson(JObject obj, string prefix = "")
    {
        var result = new Dictionary<string, string>();

        foreach (var property in obj.Properties())
        {
            var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value is JObject childObj)
            {
                // 递归扁平化子对象
                foreach (var kvp in FlattenJson(childObj, key))
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            else if (property.Value.Type == JTokenType.String)
            {
                result[key] = property.Value.Value<string>()!;
            }
        }

        return result;
    }
}
