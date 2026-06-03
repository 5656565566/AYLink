using System;
using System.Collections.Generic;
using System.Linq;
using AYLink.Core.Devices;
using AYLink.Desktop.Models;

namespace AYLink.Desktop.Services.Devices;

/// <summary>
/// 本地设备分组服务
/// 负责保存本地设备分组定义以及设备与分组之间的归属关系
/// </summary>
public sealed class LocalDeviceGroupService
{
    /// <summary>
    /// 全局单例实例
    /// </summary>
    public static LocalDeviceGroupService Instance { get; } = new();

    private const string ConfigName = "deviceGroups";
    private const string DefaultGroupId = "default";
    private const string DefaultGroupName = "默认分组";

    private readonly ConfigManager _configManager = ConfigManager.Instance;
    private readonly DeviceGroupConfig _config;

    private LocalDeviceGroupService()
    {
        _config = _configManager.LoadConfig<DeviceGroupConfig>(ConfigName) ?? new DeviceGroupConfig();
        EnsureDefaultGroup();
    }

    /// <summary>
    /// 获取当前所有本地设备分组
    /// </summary>
    /// <returns>按排序值和名称排列的分组列表</returns>
    public IReadOnlyList<DeviceGroupDescriptor> GetGroups()
    {
        return _config.Groups
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(Map)
            .ToList();
    }

    /// <summary>
    /// 创建新的本地设备分组
    /// </summary>
    /// <param name="name">分组名称</param>
    /// <returns>创建后的分组描述；名称为空时返回 null</returns>
    public DeviceGroupDescriptor? CreateGroup(string name, string description = "")
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return null;
        }

        var group = new DeviceGroupInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmedName,
            Description = description.Trim(),
            SortOrder = NextSortOrder(),
            IsSystem = false
        };

        _config.Groups.Add(group);
        Save();
        return Map(group);
    }

    /// <summary>
    /// 重命名指定本地设备分组
    /// </summary>
    /// <param name="groupId">分组 ID</param>
    /// <param name="name">新的分组名称</param>
    /// <returns>更新后的分组描述；失败时返回 null</returns>
    public DeviceGroupDescriptor? RenameGroup(string groupId, string name)
        => UpdateGroup(groupId, name, null);

    /// <summary>
    /// 更新指定本地设备分组
    /// </summary>
    /// <param name="groupId">分组 ID</param>
    /// <param name="name">新的分组名称</param>
    /// <param name="description">新的分组描述；传入 null 时保留原描述</param>
    /// <returns>更新后的分组描述；失败时返回 null</returns>
    public DeviceGroupDescriptor? UpdateGroup(string groupId, string name, string? description)
    {
        var group = FindGroup(groupId);
        var trimmedName = name.Trim();
        if (group == null || group.IsSystem || string.IsNullOrWhiteSpace(trimmedName))
        {
            return null;
        }

        group.Name = trimmedName;
        if (description != null)
        {
            group.Description = description.Trim();
        }

        Save();
        return Map(group);
    }

    /// <summary>
    /// 删除指定本地设备分组
    /// 同时清理所有设备上的该分组归属
    /// </summary>
    /// <param name="groupId">分组 ID</param>
    /// <returns>是否删除成功</returns>
    public bool DeleteGroup(string groupId)
    {
        var group = FindGroup(groupId);
        if (group == null || group.IsSystem)
        {
            return false;
        }

        _config.Groups.Remove(group);
        foreach (var item in _config.DeviceGroups.ToList())
        {
            item.Value.RemoveAll(id => string.Equals(id, group.Id, StringComparison.OrdinalIgnoreCase));
            if (item.Value.Count == 0)
            {
                _config.DeviceGroups.Remove(item.Key);
            }
        }

        Save();
        return true;
    }

    /// <summary>
    /// 获取指定本地设备所属的分组集合
    /// 未设置分组时返回默认分组
    /// </summary>
    /// <param name="serial">本地设备序列号</param>
    /// <returns>设备所属分组集合</returns>
    public IReadOnlyList<DeviceGroupDescriptor> GetGroupsForDevice(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return [Map(GetDefaultGroup())];
        }

        var key = serial.Trim();
        if (!_config.DeviceGroups.TryGetValue(key, out var groupIds) || groupIds.Count == 0)
        {
            return [Map(GetDefaultGroup())];
        }

        var groups = groupIds
            .Select(FindGroup)
            .Where(item => item != null)
            .Select(item => Map(item!))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return groups.Count == 0 ? [Map(GetDefaultGroup())] : groups;
    }

    /// <summary>
    /// 设置指定本地设备所属的分组集合
    /// 仅保留已存在的有效分组 ID
    /// </summary>
    /// <param name="serial">本地设备序列号</param>
    /// <param name="groupIds">目标分组 ID 集合</param>
    public void SetGroupsForDevice(string serial, IEnumerable<string> groupIds)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        var validGroupIds = groupIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => FindGroup(id) != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var key = serial.Trim();
        if (validGroupIds.Count == 0)
        {
            _config.DeviceGroups.Remove(key);
        }
        else
        {
            _config.DeviceGroups[key] = validGroupIds;
        }

        Save();
    }

    private void EnsureDefaultGroup()
    {
        if (_config.Groups.Any(item => string.Equals(item.Id, DefaultGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _config.Groups.Insert(0, new DeviceGroupInfo
        {
            Id = DefaultGroupId,
            Name = DefaultGroupName,
            Description = string.Empty,
            SortOrder = 0,
            IsSystem = true
        });
        Save();
    }

    private DeviceGroupInfo GetDefaultGroup()
    {
        EnsureDefaultGroup();
        return FindGroup(DefaultGroupId)!;
    }

    private DeviceGroupInfo? FindGroup(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return null;
        }

        return _config.Groups.FirstOrDefault(item => string.Equals(item.Id, groupId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private int NextSortOrder()
    {
        return _config.Groups.Count == 0 ? 10 : _config.Groups.Max(item => item.SortOrder) + 10;
    }

    private static DeviceGroupDescriptor Map(DeviceGroupInfo group)
    {
        return new DeviceGroupDescriptor
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            SortOrder = group.SortOrder,
            IsSystem = group.IsSystem
        };
    }

    private void Save()
    {
        _configManager.SaveConfig(ConfigName, _config);
    }
}
