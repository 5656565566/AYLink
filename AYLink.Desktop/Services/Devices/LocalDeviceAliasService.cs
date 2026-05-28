using System;
using AYLink.Desktop.Models;

namespace AYLink.Desktop.Services.Devices;

/// <summary>
/// 本地设备别名服务
/// 负责按设备序列号保存与读取本地设备的自定义名称
/// </summary>
public sealed class LocalDeviceAliasService
{
    /// <summary>
    /// 全局单例实例
    /// </summary>
    public static LocalDeviceAliasService Instance { get; } = new();

    private const string ConfigName = "localDeviceAliases";
    private readonly ConfigManager _configManager = ConfigManager.Instance;
    private readonly LocalDeviceAliasConfig _config;

    private LocalDeviceAliasService()
    {
        _config = _configManager.LoadConfig<LocalDeviceAliasConfig>(ConfigName) ?? new LocalDeviceAliasConfig();
    }

    /// <summary>
    /// 根据设备序列号获取已保存的别名
    /// </summary>
    public string? GetAlias(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        return _config.Aliases.TryGetValue(serial.Trim(), out var alias) && !string.IsNullOrWhiteSpace(alias)
            ? alias
            : null;
    }

    /// <summary>
    /// 保存或清除设备别名
    /// </summary>
    public void SetAlias(string serial, string alias)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        var key = serial.Trim();
        var value = alias.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (_config.Aliases.Remove(key))
            {
                Save();
            }

            return;
        }

        _config.Aliases[key] = value;
        Save();
    }

    private void Save()
    {
        _configManager.SaveConfig(ConfigName, _config);
    }
}
