using Avalonia.Controls;
using AYLink.Desktop.Services;

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
}
