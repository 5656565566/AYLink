using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;

namespace AYLink.UI;

public partial class DetachedTabWindow : Window
{
    private readonly TabPage? _tabPage;

    public DetachedTabWindow()
    {
        InitializeComponent();
    }

    public DetachedTabWindow(TabViewItem tabViewItem)
    {
        InitializeComponent();

        _tabPage = new TabPage();
        ContentFrame.Content = _tabPage;
        _tabPage.AddNewTab(tabViewItem);

        AddHandler(KeyDownEvent, ScreenPage_KeyDown, RoutingStrategies.Bubble,
            handledEventsToo: true);

        Closed += DetachedTabWindow_Closed;
    }

    private void DetachedTabWindow_Closed(object? sender, System.EventArgs e)
    {
        _tabPage?.Dispose();
    }

    public void AddTab(TabViewItem tabViewItem)
    {
        _tabPage!.AddNewTab(tabViewItem);
    }


    private void ScreenPage_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11 || sender is not Window) return;
        e.Handled = true;

        _tabPage!.ToggleFullScreen();
    }
}