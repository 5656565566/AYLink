using AYLink.Utils;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
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

        mainTabView.TabDragCompleted += MainTabView_TabDragCompleted;
        mainTabView.TabDroppedOutside += MainTabView_TabDroppedOutside;
        mainTabView.SelectionChanged += MainTabView_SelectionChanged;
    }


    private void MainTabView_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is TabView tabView)
        {
            var tabViewParentWindow = FindParentWindow(tabView);

            if (tabViewParentWindow == null) return;

            if (tabView.SelectedItem is not TabViewItem selectedTabItem) return;

            if (tabViewParentWindow is MainWindow) return;

            tabViewParentWindow.Title = (string)selectedTabItem.Header;
        }
    }

    public void Dispose()
    {
        foreach (var item in mainTabView.TabItems.Cast<object>().ToList())
        {
            if (item is TabViewItem tvi)
            {
                (tvi.Content as ScreenView)?.Dispose();
                (mainTabView.TabItems as IList)?.Remove(tvi);
            }
        }
    }

    private void MainTabView_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        if (sender.TabItems is not IList sourceItemsList) return;

        var sourceWindow = FindParentWindow(sender);
        var targetWindow = windowsManager.GetWindowUnderPointer();

        if (sourceWindow != null && targetWindow != null && sourceWindow != targetWindow)
        {
            sourceItemsList.Remove(args.Tab);

            Dispatcher.UIThread.Post(() =>
            {
                if (targetWindow is MainWindow mainWindow)
                {
                    mainWindow.AddTab(args.Tab);
                }

                if (targetWindow is DetachedTabWindow detachedTabWindow)
                {
                    detachedTabWindow.AddTab(args.Tab);
                }

                if (sourceItemsList.Count == 0)
                {
                    sourceWindow.Close();
                }
            });

            return; // ´°¿Ú¼äÒÆ¶¯
        }

        if (sourceItemsList.Count == 1 && (sourceWindow is DetachedTabWindow))
        {
            return;
        }
        if (sourceItemsList.Count == 1 && targetWindow is MainWindow mainWindow)
        {
            mainWindow.ShowTip();
        }

        DetachedTabWindow detachedTabWindow = new(args.Tab);

        sourceItemsList.Remove(args.Tab);

        if (args.Tab is TabViewItem tabViewItem)
            detachedTabWindow.Title = (string?)tabViewItem.Header;

        windowsManager.RegisterWindow(detachedTabWindow);
        detachedTabWindow.Show();
    }


    private void MainTabView_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        switch (args.DropResult)
        {
            case DragDropEffects.None:
                break;
            case DragDropEffects.Copy:
                break;
            case DragDropEffects.Move:
                MoveTabAcrossWindows(sender, args.Tab);
                break;
            case DragDropEffects.Link:
                break;
            default:
                break;
        }
    }

    private void MoveTabAcrossWindows(TabView sourceTabView, TabViewItem tvi)
    {
        if (sourceTabView.TabItems is not IList sourceItems) return;

        var sourceWnd = FindParentWindow(sourceTabView);
        var targetWnd = windowsManager.GetWindowUnderPointer();

        if (sourceWnd is null || targetWnd is null || sourceWnd == targetWnd) return;
        sourceItems.Remove(tvi);

        Dispatcher.UIThread.Post(() =>
        {
            switch (targetWnd)
            {
                case MainWindow mw: mw.AddTab(tvi); break;
                case DetachedTabWindow dw: dw.AddTab(tvi); break;
            }

            if (sourceItems.Count != 0) return;

            switch (sourceWnd)
            {
                case DetachedTabWindow: sourceWnd.Close(); break;
                case MainWindow mw: mw.ShowTip(); break;
            }
        });
    }
    public void AddNewTab(string header, UserControl content, bool onlyTip = false)
    {
        var existingTvi = mainTabView.TabItems.OfType<TabViewItem>().FirstOrDefault(t => t.Header as string == header);
        if (existingTvi != null)
        {
            mainTabView.SelectedItem = existingTvi;
            return;
        }

        var newTabItem = new TabViewItem
        {
            Header = header,
            Content = content
        };

        var items = mainTabView.TabItems as IList;

        if (!onlyTip)
        {
            newTabItem.CloseRequested += NewTabItem_CloseRequested;
        }
        else
        {
            items?.Clear();
        }

        items?.Add(newTabItem);
        mainTabView.SelectedItem = newTabItem;
    }

    public void AddNewTab(TabViewItem tabViewItem)
    {
        if (mainTabView.TabItems is IList items) items.Add(tabViewItem);
    }

    private void NewTabItem_CloseRequested(TabViewItem sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tabToClose = args.Tab ?? sender;

        var parentTabView = FindParentTabView(tabToClose);
        if (parentTabView == null)
            return;

        if (parentTabView.TabItems is not IList itemsList)
            return;

        if (args.Tab == null) return;

        (args.Tab.Content as ScreenView)!.Dispose();

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

    private static TabView? FindParentTabView(TabViewItem tabItem)
    {
        var parent = tabItem.Parent;
        while (parent != null)
        {
            if (parent is TabView tabView)
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
    private TabViewItem? _sourceTab;

    public void ToggleFullScreen()
    {
        if (mainTabView.TabItems is not IList sourceItemsList) return;

        if (_fullscreenWnd is { } wnd)
        {
            wnd.Closed -= FullScreenWindow_Closed;

            var content = (UserControl)wnd.Content!;
            wnd.Content = null;
            wnd.Close();

            AddNewTab(_sourceTab!);

            FindParentWindow(mainTabView)!.IsVisible = true;
            _fullscreenWnd = null;
            _sourceTab = null;
            return;
        }

        if (mainTabView.SelectedItem is not TabViewItem tab ||
            tab.Content is not UserControl view)
            return;

        _sourceTab = tab;

        _sourceTab = (TabViewItem?)mainTabView.SelectedItem;
        sourceItemsList.Remove(_sourceTab);

        var host = new Window
        {
            WindowState = WindowState.Maximized,
            SystemDecorations = SystemDecorations.None,
            Topmost = true,
            Content = _sourceTab!.Content
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

        FindParentWindow(mainTabView)!.IsVisible = false;

        _fullscreenWnd = host;
        host.Show();
    }
    private void FullScreenWindow_Closed(object? sender, EventArgs e)
    {
        ToggleFullScreen();
    }
}