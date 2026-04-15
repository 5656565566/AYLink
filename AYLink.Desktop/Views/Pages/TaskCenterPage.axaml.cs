using Avalonia.Controls;
using Avalonia.Input;
using AYLink.Desktop.ViewModels.Pages;

namespace AYLink.Desktop.Views.Pages;

/// <summary>
/// 任务管理页视图
/// </summary>
public partial class TaskCenterPage : UserControl
{
    public TaskCenterPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 搜索框回车键处理 - 按下回车时执行搜索命令
    /// </summary>
    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TaskCenterPageViewModel vm)
        {
            vm.SearchCommand.Execute(null);
        }
    }
}
