using Avalonia.Controls;
using AYLink.Desktop.ViewModels.Pages;

namespace AYLink.Desktop.Views.Pages;

public partial class FilePage : UserControl
{
    public FilePageViewModel? ViewModel => DataContext as FilePageViewModel;

    public FilePage()
    {
        InitializeComponent();
    }
}
