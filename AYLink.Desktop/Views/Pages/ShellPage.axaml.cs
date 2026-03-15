using AYLink.Controls.Terminal;
using AYLink.Desktop.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AYLink.Desktop.Views.Pages;

public partial class ShellPage : UserControl
{
    public ShellPage()
    {
        InitializeComponent();
    }

    private void OnTerminalLoaded(object? sender, RoutedEventArgs e)
    {
        // 控件加载到屏幕时 自动与它专属的 TabViewModel 绑定
        if (sender is TerminalControl terminal && terminal.DataContext is ShellTabViewModel tabVm)
        {
            tabVm.AttachTerminal(terminal);
        }
    }

    private void OnTerminalUnloaded(object? sender, RoutedEventArgs e)
    {
        // 控件从屏幕移除时 解绑以释放内存并阻止事件多重触发
        if (sender is TerminalControl terminal && terminal.DataContext is ShellTabViewModel tabVm)
        {
            tabVm.DetachTerminal();
        }
    }
}