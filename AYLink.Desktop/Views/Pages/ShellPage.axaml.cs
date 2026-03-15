using AYLink.Controls.Terminal;
using AYLink.Desktop.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;

namespace AYLink.Desktop.Views.Pages;

public partial class ShellPage : UserControl
{
    public ShellPage()
    {
        InitializeComponent();

        // 监听 DataContext 变化以订阅 SelectedTab 变更
        DataContextChanged += OnDataContextChanged;
    }

    private ShellPageViewModel? _viewModel;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        // 解除旧订阅
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = DataContext as ShellPageViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellPageViewModel.SelectedTab))
        {
            // 当 SelectedTab 变化时延迟到渲染后再绑定终端控件
            if (_viewModel?.SelectedTab != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TryAttachTerminal(_viewModel.SelectedTab);
                }, Avalonia.Threading.DispatcherPriority.Render);
            }
        }
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
