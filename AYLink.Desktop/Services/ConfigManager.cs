using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AYLink.Desktop.Services;

public sealed class ConfigManager
{
    private static readonly Lazy<ConfigManager> _instance = new(() => new ConfigManager());

    private readonly string _path;
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    private ConfigManager()
    {
        _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

        if (!Directory.Exists(_path))
        {
            Directory.CreateDirectory(_path);
        }
    }

    public static ConfigManager Instance => _instance.Value;

    public void SaveConfig<T>(string configName, T configObject)
    {
        if (string.IsNullOrWhiteSpace(configName))
            throw new ArgumentException("Config name cannot be empty", nameof(configName));

        var filePath = GetConfigFilePath(configName);
        string json = JsonConvert.SerializeObject(configObject, _jsonSettings);
        File.WriteAllText(filePath, json);
    }

    public T LoadConfig<T>(string configName) where T : new()
    {
        if (string.IsNullOrWhiteSpace(configName))
            throw new ArgumentException("Config name cannot be empty", nameof(configName));

        var filePath = GetConfigFilePath(configName);

        if (!File.Exists(filePath))
            return new T();

        string json = File.ReadAllText(filePath);
        return JsonConvert.DeserializeObject<T>(json) ?? new T();
    }

    public bool DeleteConfig(string configName)
    {
        if (string.IsNullOrWhiteSpace(configName))
            throw new ArgumentException("Config name cannot be empty", nameof(configName));

        var filePath = GetConfigFilePath(configName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }

        return false;
    }

    public IEnumerable<string?> ListConfigs()
    {
        if (!Directory.Exists(_path))
            return Enumerable.Empty<string>();

        return Directory.GetFiles(_path, "*.json")
                       .Select(Path.GetFileNameWithoutExtension);
    }

    public bool ConfigExists(string configName)
    {
        if (string.IsNullOrWhiteSpace(configName))
            return false;

        var filePath = GetConfigFilePath(configName);
        return File.Exists(filePath);
    }

    public string Serialize<T>(T obj)
    {
        return JsonConvert.SerializeObject(obj, _jsonSettings);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json)!;
    }

    private string GetConfigFilePath(string configName)
    {
        var safeName = string.Join("_", configName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_path, $"{safeName}.json");
    }

    public bool HasChanges<T>(T original, T modified)
    {
        if (ReferenceEquals(original, null) || ReferenceEquals(modified, null))
            return !ReferenceEquals(original, modified);

        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            var originalValue = property.GetValue(original);
            var modifiedValue = property.GetValue(modified);

            if (!Equals(originalValue, modifiedValue))
                return true;
        }

        return false;
    }
}
