namespace AYLink.Desktop.Views.Pages;

public partial class ScreenTabContentView : UserControl
{
    private Window? _fullScreenWindow;

    public ScreenTabContentView()
    {
        InitializeComponent();
    }

    private void OnFullScreenClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScreenTabViewModel viewModel) return;

        // 如果已经在全屏窗口中 则关闭它
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window win && WindowIsFullScreenWindow(win))
        {
            win.Close();
            return;
        }

        // 创建全屏新窗口
        _fullScreenWindow = new Window
        {
            Title = viewModel.Title,
            WindowState = WindowState.FullScreen,
            SystemDecorations = SystemDecorations.None,
            Topmost = true,
            Background = Avalonia.Media.Brushes.Black,
            DataContext = viewModel,
            Content = new ScreenTabContentView() // 复用当前的 UserControl
        };

        // 按 Esc 退出全屏
        _fullScreenWindow.KeyDown += (s, ev) =>
        {
            if (ev.Key == Key.Escape)
            {
                _fullScreenWindow.Close();
                ev.Handled = true;
            }
        };

        _fullScreenWindow.Closed += (s, ev) =>
        {
            _fullScreenWindow = null;
            // 全屏窗口关闭后 重新唤醒原界面的 Image 绑定
            var videoImage = this.FindControl<Image>("VideoImage");
            if (videoImage != null)
            {
                viewModel.AttachVideoImage(videoImage);
            }
        };

        _fullScreenWindow.Show();
    }

    private bool WindowIsFullScreenWindow(Window win)
    {
        return win.SystemDecorations == SystemDecorations.None && win.WindowState == WindowState.FullScreen;
    }
}