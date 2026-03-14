using Avalonia.Controls;
using AYLink.Desktop.ViewModels.Pages;
using FluentAvalonia.UI.Controls;

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
}
