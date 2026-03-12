using AYLink.Utils;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AYLink.Controls;
using System;
using System.Collections;
using System.Linq;

namespace AYLink.UI;

public partial class TabPage : UserControl
{
    private readonly WindowsManager windowsManager = WindowsManager.Instance;

    public TabPage()
    {
        InitializeComponent();

        mainTabView.TabDraggedOutside += MainTabView_TabDraggedOutside;
        mainTabView.SelectionChanged += MainTabView_SelectionChanged;
        mainTabView.TabCloseRequested += MainTabView_TabCloseRequested;
    }


    private void MainTabView_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (sender is BrowserTabView tabView)
        {
            var tabViewParentWindow = FindParentWindow(tabView);

            if (tabViewParentWindow == null) return;

            if (tabView.SelectedItem is not BrowserTabItem selectedTabItem) return;

            if (tabViewParentWindow is MainWindow) return;

            tabViewParentWindow.Title = (string?)selectedTabItem.Header;
        }
    }

    public void Dispose()
    {
        foreach (var item in mainTabView.Items.Cast<object>().ToList())
        {
            if (item is BrowserTabItem tvi)
            {
                (tvi.Content as ScreenView)?.Dispose();
                (mainTabView.Items as IList)?.Remove(tvi);
            }
        }
    }

    private void MainTabView_TabDraggedOutside(object? sender, TabDraggedOutsideEventArgs args)
    {
        if (mainTabView.Items is not IList sourceItemsList) return;

        var sourceWindow = FindParentWindow(mainTabView);
        var targetWindow = windowsManager.GetWindowUnderPointer();

        if (sourceWindow != null && targetWindow != null && sourceWindow != targetWindow)
        {
            sourceItemsList.Remove(args.TabItem);

            Dispatcher.UIThread.Post(() =>
            {
                if (targetWindow is MainWindow mainWindow)
                {
                    mainWindow.AddTab(args.TabItem);
                }

                if (targetWindow is DetachedTabWindow detachedTabWindow)
                {
                    detachedTabWindow.AddTab(args.TabItem);
                }

                if (sourceItemsList.Count == 0)
                {
                    sourceWindow.Close();
                }
            });

            return;
        }

        if (sourceItemsList.Count == 1 && (sourceWindow is DetachedTabWindow))
        {
            return;
        }
        if (sourceItemsList.Count == 1 && targetWindow is MainWindow mainWindow2)
        {
            mainWindow2.ShowTip();
        }

        DetachedTabWindow detachedTabWindowNew = new(args.TabItem);

        sourceItemsList.Remove(args.TabItem);

        detachedTabWindowNew.Title = (string?)args.TabItem.Header;

        windowsManager.RegisterWindow(detachedTabWindowNew);
        detachedTabWindowNew.Show();
    }

    public void AddNewTab(string header, UserControl content, bool onlyTip = false)
    {
        var existingTvi = mainTabView.Items.OfType<BrowserTabItem>().FirstOrDefault(t => t.Header as string == header);
        if (existingTvi != null)
        {
            mainTabView.SelectedItem = existingTvi;
            return;
        }

        var newTabItem = new BrowserTabItem
        {
            Header = header,
            Content = content
        };

        var items = mainTabView.Items as IList;

        if (onlyTip)
        {
            items?.Clear();
        }

        items?.Add(newTabItem);
        mainTabView.SelectedItem = newTabItem;
    }

    public void AddNewTab(BrowserTabItem tabViewItem)
    {
        if (mainTabView.Items is IList items) items.Add(tabViewItem);
    }

    private void MainTabView_TabCloseRequested(object? sender, TabCloseRequestedEventArgs args)
    {
        var tabToClose = args.TabItem;

        var parentTabView = FindParentTabView(tabToClose);
        if (parentTabView == null)
            return;

        if (parentTabView.Items is not IList itemsList)
            return;

        (tabToClose.Content as ScreenView)?.Dispose();

        if (parentTabView.SelectedItem == tabToClose)
        {
            itemsList.Remove(tabToClose);
            if (itemsList.Count > 0)
            {
                parentTabView.SelectedIndex = 0;
            }
        }
        else
        {
            itemsList.Remove(tabToClose);
        }

        var parentWindow = FindParentWindow(parentTabView);

        if (itemsList.Count == 0 && parentWindow is DetachedTabWindow)
        {
            var tabViewParentWindow = FindParentWindow(parentTabView);
            tabViewParentWindow?.Close();
        }
        else if (itemsList.Count == 0 && parentWindow is MainWindow mainWindow)
        {
            mainWindow.ShowTip();
        }
    }

    private static BrowserTabView? FindParentTabView(BrowserTabItem tabItem)
    {
        var parent = tabItem.Parent;
        while (parent != null)
        {
            if (parent is BrowserTabView tabView)
            {
                return tabView;
            }
            parent = parent.Parent;
        }
        return null;
    }

    private static Window? FindParentWindow(Control control)
    {
        var current = control;
        while (current != null)
        {
            if (current is Window window)
            {
                return window;
            }
            current = current.Parent as Control;
        }
        return null;
    }

    private Window? _fullscreenWnd;
    private BrowserTabItem? _sourceTab;

    public void ToggleFullScreen()
    {
        if (mainTabView.Items is not IList sourceItemsList) return;

        if (_fullscreenWnd is { } wnd)
        {
            wnd.Closed -= FullScreenWindow_Closed;

            var content = (UserControl)wnd.Content!;
            wnd.Content = null;
            wnd.Close();

            if (_sourceTab != null)
            {
                AddNewTab(_sourceTab);
            }

            var parentWindow = FindParentWindow(mainTabView);
            if (parentWindow != null)
            {
                parentWindow.IsVisible = true;
            }
            _fullscreenWnd = null;
            _sourceTab = null;
            return;
        }

        if (mainTabView.SelectedItem is not BrowserTabItem tab ||
            tab.Content is not UserControl view)
            return;

        _sourceTab = tab;

        _sourceTab = (BrowserTabItem?)mainTabView.SelectedItem;
        sourceItemsList.Remove(_sourceTab);

        var host = new Window
        {
            WindowState = WindowState.Maximized,
            SystemDecorations = SystemDecorations.None,
            Topmost = true,
            Content = _sourceTab?.Content
        };

        host.AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.F11)
                {
                    e.Handled = true;
                    ToggleFullScreen();
                }
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true
        );
        
        host.Closed += FullScreenWindow_Closed;

        var parentWindow2 = FindParentWindow(mainTabView);
        if (parentWindow2 != null)
        {
            parentWindow2.IsVisible = false;
        }

        _fullscreenWnd = host;
        host.Show();
    }
    private void FullScreenWindow_Closed(object? sender, EventArgs e)
    {
        ToggleFullScreen();
    }
}