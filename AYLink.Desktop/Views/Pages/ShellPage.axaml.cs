using AYLink.Controls.Terminal;
using AYLink.Desktop.Services;
using AYLink.Desktop.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using System.Linq;

namespace AYLink.Desktop.Views.Pages;


public partial class ShellPage : UserControl
{
    private readonly BackgroundImageManager backgroundImageManager = BackgroundImageManager.Instance;

    public ShellPage()
    {
        InitializeComponent();
        backgroundImageManager.RegisterImageComponent(BackgroundImage);
        backgroundImageManager.SetRandomBackgroundImage();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        backgroundImageManager.UnregisterImageComponent(BackgroundImage);
    }

    /// <summary>
    /// 标签页关闭请求
    /// </summary>
    private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is ShellTabViewModel tab)
        {
            tab.CloseTabCommand.Execute(null);
        }
    }

    /// <summary>
    /// 标签页选择变化 - 绑定终端控件到 ViewModel
    /// </summary>
    private void TabView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 分离旧选项卡
        foreach (var item in e.RemovedItems)
        {
            var tab = ExtractTabViewModel(item);
            tab?.DetachTerminal();
        }

        // 附加新标签页的终端控件
        foreach (var item in e.AddedItems)
        {
            var newTab = ExtractTabViewModel(item);
            if (newTab != null)
            {
                // 延迟到模板渲染完成后再查找 TerminalControl
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TryAttachTerminal(newTab);
                }, Avalonia.Threading.DispatcherPriority.Render);
            }
        }
    }

    /// <summary>
    /// 从 TabView 的选中项中提取 ShellTabViewModel
    /// TabView 可能传递 TabViewItem 包装器或直接的 ViewModel
    /// </summary>
    private static ShellTabViewModel? ExtractTabViewModel(object? item)
    {
        if (item is ShellTabViewModel vm)
            return vm;
        if (item is TabViewItem tvi && tvi.DataContext is ShellTabViewModel tabVm)
            return tabVm;
        return null;
    }

    /// <summary>
    /// 尝试在可视树中查找标签页对应的 TerminalControl 并绑定
    /// </summary>
    private void TryAttachTerminal(ShellTabViewModel tab)
    {
        // 从可视树中查找当前可见的 TerminalControl
        var terminal = this.GetVisualDescendants()
            .OfType<TerminalControl>()
            .FirstOrDefault();

        if (terminal != null)
        {
            tab.AttachTerminal(terminal);
        }
    }
}
