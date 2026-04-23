using Avalonia.Controls;

namespace AYLink.Desktop.Views.Pages;

/// <summary>
/// 任务管理页视图
/// </summary>
public partial class TaskPage : UserControl
{
    public TaskPage()
    {
        InitializeComponent();
    }

    private void SearchBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && sender is TextBox textBox && textBox.DataContext is ViewModels.Pages.TaskTabViewModel vm)
        {
            vm.SearchText = textBox.Text ?? string.Empty;
            vm.SearchCommand.Execute(null);
        }
    }
}
