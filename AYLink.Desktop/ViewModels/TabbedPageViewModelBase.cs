using AYLink.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// 带标签页管理的页面 ViewModel 泛型基类
/// 提供 Tabs、SelectedTab、AddNewTab、关闭/导航等通用逻辑
/// </summary>
/// <typeparam name="TTab">具体的标签页 ViewModel 类型</typeparam>
public abstract partial class TabbedPageViewModelBase<TTab> : PageViewModelBase
    where TTab : TabItemViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<TTab> _tabs = [];

    /// <summary>
    /// 内部存储当前选中的 Tab
    /// 使用自定义属性而非 [ObservableProperty] 以便在 setter 中进行保护
    /// </summary>
    private TTab? _selectedTab;

    /// <summary>
    /// 当前选中的标签页
    /// 防止 TabView 重排/视图切换时意外置 null
    /// </summary>
    public TTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            // 防止 TabView 重排/视图切换时将 SelectedTab 意外置 null
            // 只有在显式关闭所有标签页（Tabs.Count==0）时才允许设为 null
            if (value == null && Tabs.Count > 0)
            {
                // 如果当前选中的 Tab 仍然在集合中，保持不变
                if (_selectedTab != null && Tabs.Contains(_selectedTab))
                    return;
                // 否则选择最后一个
                value = Tabs.LastOrDefault();
            }
            SetProperty(ref _selectedTab, value);
        }
    }

    /// <summary>
    /// 是否显示添加标签页按钮（默认 false）
    /// </summary>
    public virtual bool IsAddTabButtonVisible => false;

    /// <summary>
    /// 空状态图标（FluentAvalonia Symbol 名称）
    /// </summary>
    public abstract string EmptyStateIcon { get; }

    /// <summary>
    /// 空状态标题
    /// </summary>
    public abstract string EmptyStateTitle { get; }

    /// <summary>
    /// 空状态描述
    /// </summary>
    public abstract string EmptyStateDescription { get; }

    /// <summary>
    /// 是否允许同一设备开多个标签页（投屏=true，其他=false）
    /// </summary>
    protected virtual bool AllowDuplicateDeviceTabs => false;

    /// <summary>
    /// 工厂方法：由子类创建具体的 Tab ViewModel
    /// </summary>
    protected abstract TTab CreateTab(DeviceModel device);

    /// <summary>
    /// Tab 关闭后的附加清理（如 Dispose）由子类重写
    /// </summary>
    protected virtual void OnTabClosed(TTab tab) { }

    /// <summary>
    /// Tab 关闭前的拦截（如 FilePage 至少保留一个 Tab）
    /// 返回 true 允许关闭，返回 false 阻止关闭
    /// </summary>
    protected virtual bool OnTabClosing(TTab tab) => true;

    /// <summary>
    /// 添加新标签页（通过 DeviceModel 工厂方法创建）
    /// </summary>
    [RelayCommand]
    protected virtual void AddNewTab(DeviceModel device)
    {
        var newTab = CreateTab(device);
        RegisterTab(newTab);
    }

    /// <summary>
    /// 注册 Tab 到集合并订阅关闭事件 - 供子类在自定义创建 Tab 时调用
    /// </summary>
    protected void RegisterTab(TTab tab)
    {
        tab.OnCloseRequested += OnTabCloseRequested;
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    /// <summary>
    /// 从集合中移除 Tab（不触发 OnTabClosed），用于标签页脱离到独立窗口
    /// 解除事件订阅但不执行清理逻辑
    /// </summary>
    public bool DetachTab(TabItemViewModelBase tab)
    {
        if (tab is not TTab typedTab) return false;
        if (!Tabs.Contains(typedTab)) return false;

        typedTab.OnCloseRequested -= OnTabCloseRequested;
        Tabs.Remove(typedTab);

        if (Tabs.Count == 0)
        {
            ForceSetSelectedTab(null);
        }
        else
        {
            SelectedTab ??= Tabs.LastOrDefault();
        }

        return true;
    }

    /// <summary>
    /// 将外部 Tab 插入集合（用于从独立窗口合并回来），支持任意 TabItemViewModelBase 类型
    /// </summary>
    public bool AttachTab(TabItemViewModelBase tab, int index = -1)
    {
        if (tab is not TTab typedTab) return false;

        typedTab.OnCloseRequested += OnTabCloseRequested;
        if (index >= 0 && index <= Tabs.Count)
            Tabs.Insert(index, typedTab);
        else
            Tabs.Add(typedTab);

        SelectedTab = typedTab;
        return true;
    }

    /// <summary>
    /// 强制清空选中（仅在 Dispose/全部关闭时使用）
    /// 绕过 SelectedTab setter 的保护逻辑
    /// </summary>
    protected void ForceSetSelectedTab(TTab? tab)
    {
        SetProperty(ref _selectedTab, tab, nameof(SelectedTab));
    }

    /// <summary>
    /// 统一的 Tab 关闭处理
    /// </summary>
    private void OnTabCloseRequested(TabItemViewModelBase tab)
    {
        if (tab is not TTab typedTab) return;

        // 允许子类拦截关闭
        if (!OnTabClosing(typedTab)) return;

        typedTab.OnCloseRequested -= OnTabCloseRequested;
        OnTabClosed(typedTab);
        Tabs.Remove(typedTab);

        if (Tabs.Count == 0)
        {
            ForceSetSelectedTab(null);
        }
        else
        {
            SelectedTab ??= Tabs.LastOrDefault();
        }
    }

    /// <summary>
    /// 页面导航到时 - 自动查找或创建设备标签页
    /// </summary>
    public override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);

        if (parameter is DeviceModel device)
        {
            if (!AllowDuplicateDeviceTabs)
            {
                var existing = Tabs.FirstOrDefault(t => t.Device?.Serial == device.Serial);
                if (existing != null)
                {
                    SelectedTab = existing;
                    return;
                }
            }
            AddNewTabCommand.Execute(device);
        }
    }
}
