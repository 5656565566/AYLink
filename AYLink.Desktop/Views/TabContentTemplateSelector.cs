using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using AYLink.Desktop.ViewModels;
using System;
using System.Collections.Generic;

namespace AYLink.Desktop.Views;

/// <summary>
/// 标签页内容模板选择器
/// 根据 TabItemViewModelBase 的具体类型选择合适的 DataTemplate
/// 用于 DetachedTabWindow 中支持混合不同类型的标签页
/// </summary>
public class TabContentTemplateSelector : IDataTemplate
{
    /// <summary>
    /// ViewModel 类型 -> DataTemplate 的映射
    /// </summary>
    private readonly Dictionary<Type, IDataTemplate> _templates = [];

    /// <summary>
    /// 默认模板（当找不到匹配类型时使用）
    /// </summary>
    public IDataTemplate? DefaultTemplate { get; set; }

    /// <summary>
    /// 注册一个 ViewModel 类型对应的 DataTemplate
    /// </summary>
    public void Register(Type vmType, IDataTemplate template)
    {
        _templates[vmType] = template;
    }

    /// <summary>
    /// 注册一个 ViewModel 类型对应的 DataTemplate
    /// </summary>
    public void Register<T>(IDataTemplate template) where T : TabItemViewModelBase
    {
        _templates[typeof(T)] = template;
    }

    public Control? Build(object? param)
    {
        if (param == null) return null;

        var type = param.GetType();

        // 精确匹配
        if (_templates.TryGetValue(type, out var template))
        {
            return template.Build(param);
        }

        // 继承链匹配
        foreach (var kvp in _templates)
        {
            if (kvp.Key.IsAssignableFrom(type))
            {
                return kvp.Value.Build(param);
            }
        }

        // 使用默认模板
        if (DefaultTemplate != null)
        {
            return DefaultTemplate.Build(param);
        }

        // 最终回退：显示标题
        return new TextBlock { Text = param?.ToString() ?? "Unknown Tab" };
    }

    public bool Match(object? data)
    {
        if (data == null) return false;
        
        var type = data.GetType();

        // 精确匹配
        if (_templates.ContainsKey(type)) return true;

        // 继承链匹配
        foreach (var kvp in _templates)
        {
            if (kvp.Key.IsAssignableFrom(type)) return true;
        }

        // 默认模板也算匹配
        return DefaultTemplate != null;
    }
}
