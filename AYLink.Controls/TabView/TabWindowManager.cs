using Avalonia;
using Avalonia.Controls;

namespace AYLink.Controls;

/// <summary>
/// 全局标签页窗口管理器，负责跟踪所有包含 BrowserTabView 的窗口实例
/// 支持跨窗口的拖拽合并（Merge）操作
/// </summary>
public sealed class TabWindowManager
{
    private static readonly Lazy<TabWindowManager> _instance = new(() => new TabWindowManager());
    public static TabWindowManager Instance => _instance.Value;

    private readonly List<BrowserTabView> _registeredTabViews = new();

    private TabWindowManager() { }

    /// <summary>
    /// 注册一个 BrowserTabView 实例
    /// </summary>
    public void Register(BrowserTabView tabView)
    {
        if (!_registeredTabViews.Contains(tabView))
        {
            _registeredTabViews.Add(tabView);
        }
    }

    /// <summary>
    /// 注销一个 BrowserTabView 实例
    /// </summary>
    public void Unregister(BrowserTabView tabView)
    {
        _registeredTabViews.Remove(tabView);
    }

    /// <summary>
    /// 获取所有已注册的 BrowserTabView 实例
    /// </summary>
    public IReadOnlyList<BrowserTabView> RegisteredTabViews => _registeredTabViews.AsReadOnly();

    /// <summary>
    /// 根据屏幕坐标查找鼠标下方的 BrowserTabView（排除指定的源 TabView）
    /// </summary>
    public BrowserTabView? FindTabViewUnderPoint(PixelPoint screenPoint, BrowserTabView? exclude = null)
    {
        foreach (var tabView in _registeredTabViews)
        {
            if (tabView == exclude) continue;
            if (!tabView.IsVisible) continue;

            var topLevel = TopLevel.GetTopLevel(tabView);
            if (topLevel is not Window window) continue;
            if (!window.IsVisible) continue;

            try
            {
                // 将屏幕坐标转换为 TabView 的本地坐标
                var localPoint = tabView.PointToClient(screenPoint);

                // 检查点是否在 TabView 的标签栏区域内（上方区域，高度约 44px）
                var tabBarHeight = 44.0; // 标签栏大致高度
                var tabBarBounds = new Rect(0, 0, tabView.Bounds.Width, tabBarHeight);

                if (tabBarBounds.Contains(localPoint))
                {
                    return tabView;
                }
            }
            catch
            {
                // PointToClient 可能在控件未附加到可视化树时抛出异常
                continue;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据屏幕坐标获取 TabView 中的目标插入索引
    /// </summary>
    public int GetInsertIndexAtPoint(BrowserTabView tabView, PixelPoint screenPoint)
    {
        try
        {
            var localPoint = tabView.PointToClient(screenPoint);
            var panel = tabView.GetAnimatedPanel();
            if (panel == null) return -1;

            double x = localPoint.X;
            double currentX = 0;

            for (int i = 0; i < panel.Children.Count; i++)
            {
                var child = panel.Children[i];
                if (!child.IsVisible) continue;
                var center = currentX + child.DesiredSize.Width / 2;
                if (x < center) return i;
                currentX += child.DesiredSize.Width + panel.Spacing;
            }

            return panel.Children.Count;
        }
        catch
        {
            return -1;
        }
    }
}
