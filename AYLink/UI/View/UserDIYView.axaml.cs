using AYLink.UI.Themes;
using Avalonia.Controls;

namespace AYLink.UI;

public partial class UserDIYView : UserControl
{
    readonly BackgroundImageManager backgroundImageManager = BackgroundImageManager.Instance;

    public UserDIYView()
    {
        InitializeComponent();

        backgroundImageManager.RegisterImageComponent(BackgroundImage);
    }
}