using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AYLink.Desktop.ViewModels.Pages;
using FluentAvalonia.UI.Controls;
using System.Linq;

namespace AYLink.Desktop.Views.Pages;

public partial class ScreenPage : UserControl
{
    public ScreenPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 标签页关闭请求
    /// </summary>
    private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is ScreenTabViewModel tab)
        {
            tab.CloseTabCommand.Execute(null);
        }
    }

    /// <summary>
    /// 标签页选择变化 - 绑定视频控件到 ViewModel
    /// </summary>
    private void TabView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 分离旧选项卡
        foreach (var item in e.RemovedItems)
        {
            var tab = ExtractTabViewModel(item);
            tab?.DetachVideoImage();
        }

        // 附加新标签页的视频控件
        foreach (var item in e.AddedItems)
        {
            var newTab = ExtractTabViewModel(item);
            if (newTab != null)
            {
                // 延迟到模板渲染完成后再查找控件
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TryAttachVideoImage(newTab);
                }, Avalonia.Threading.DispatcherPriority.Render);
            }
        }
    }

    /// <summary>
    /// 从 TabView 的选中项中提取 ScreenTabViewModel
    /// </summary>
    private static ScreenTabViewModel? ExtractTabViewModel(object? item)
    {
        if (item is ScreenTabViewModel vm)
            return vm;
        if (item is TabViewItem tvi && tvi.DataContext is ScreenTabViewModel tabVm)
            return tabVm;
        return null;
    }

    /// <summary>
    /// 尝试在可视树中查找标签页对应的视频控件并绑定
    /// </summary>
    private void TryAttachVideoImage(ScreenTabViewModel tab)
    {
        // 查找当前可见的 VideoImage
        var videoImage = this.GetVisualDescendants()
            .OfType<Image>()
            .FirstOrDefault(i => i.Name == "VideoImage");

        if (videoImage != null)
        {
            tab.AttachVideoImage(videoImage);
        }
    }
}
