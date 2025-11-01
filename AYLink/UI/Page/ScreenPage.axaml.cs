using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace AYLink.UI;

public partial class ScreenPage : UserControl
{
    private readonly TabPage _tabPage = new();
    private readonly UserDIYView _userDIYView = new();

    public ScreenPage()
    {
        InitializeComponent();

        ContentFrame.Content = _userDIYView;
    }

    public void AddNewTab(string header, UserControl userControl)
    {
        ContentFrame.Content = _tabPage;
        _tabPage.AddNewTab(header, userControl);
    }

    public void AddNewTab(TabViewItem tabViewItem)
    {
        ContentFrame.Content = _tabPage;
        _tabPage.AddNewTab(tabViewItem);
    }

    public void ShowTip()
    {
        ContentFrame.Content = _userDIYView;
    }

    public void Dispose()
    {
        _tabPage.Dispose();
    }
}