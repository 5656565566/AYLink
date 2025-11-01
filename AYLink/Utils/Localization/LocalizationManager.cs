using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AYLink.Utils.Localization;

public record LanguageInfo(string Culture, string DisplayName);

public class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private readonly string _languageFolderPath;
    private const string LanguageNameKey = "LanguageName";

    public Dictionary<string, string> Strings { get; private set; } = [];
    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Strings.TryGetValue(key, out var value) ? value : $"#{key}#";

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (Equals(_currentCulture, value)) return;
            _currentCulture = value;
            LoadLanguage(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Strings)));
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
    /// 扫描语言文件夹，列出所有可用的语言。
    /// </summary>
    /// <returns>一个包含所有可用语言信息的列表。</returns>
    public List<LanguageInfo> ListAvailableLanguages()
    {
        var languages = new List<LanguageInfo>();
        var files = Directory.GetFiles(_languageFolderPath, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var tempDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                var culture = Path.GetFileNameWithoutExtension(file);
                var displayName = tempDict!.TryGetValue(LanguageNameKey, out var name) ? name : culture;

                languages.Add(new LanguageInfo(culture, displayName));
            }
            catch { }
        }

        return [.. languages.OrderBy(l => l.DisplayName)]; // 按名称排序
    }

    private void LoadLanguage(CultureInfo culture)
    {
        var filePath = Path.Combine(_languageFolderPath, $"{culture.Name}.json");
        Dictionary<string, string> newStrings;
        try
        {
            if (!File.Exists(filePath))
            {
                if (culture.Name != "zh-CN") LoadLanguage(new CultureInfo("zh-CN"));
                return;
            }

            var json = File.ReadAllText(filePath);
            newStrings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? [];
            if (newStrings.ContainsKey(LanguageNameKey)) newStrings.Remove(LanguageNameKey);
        }
        catch
        {
            newStrings = [];
        }

        Strings = newStrings;
    }
}