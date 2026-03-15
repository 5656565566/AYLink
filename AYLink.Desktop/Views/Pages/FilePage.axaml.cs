using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AYLink.Core.Models;
using AYLink.Desktop.ViewModels.Pages;
using System.Linq;

namespace AYLink.Desktop.Views.Pages;

public partial class FilePage : UserControl
{
    public FilePage()
    {
        InitializeComponent();

        // 注册拖拽路由事件（冒泡到 UserControl 级别处理）
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // ---- 双击事件 ----

    private void LeftItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FileSystemModel file }
            && DataContext is FilePageViewModel vm)
        {
            vm.LeftItemDoubleTappedCommand.Execute(file);
        }
    }

    private void RightItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FileSystemModel file }
            && DataContext is FilePageViewModel vm)
        {
            vm.RightItemDoubleTappedCommand.Execute(file);
        }
    }

    // ---- 左侧面板右键菜单 ----

    private void LeftCtxOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilePageViewModel vm
            && vm.SelectedTab?.LeftPane.SelectedFile is FileSystemModel file)
        {
            vm.LeftCtxOpenCommand.Execute(file);
        }
    }

    private void LeftCtxCopyToRight_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilePageViewModel vm
            && vm.SelectedTab?.LeftPane.SelectedFile is FileSystemModel file)
        {
            vm.LeftCtxCopyToRightCommand.Execute(file);
        }
    }

    private void LeftCtxDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilePageViewModel vm
            && vm.SelectedTab?.LeftPane.SelectedFile is FileSystemModel file)
        {
            vm.LeftCtxDeleteCommand.Execute(file);
        }
    }

    // ---- 右侧面板右键菜单 ----

    private void RightCtxOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilePageViewModel vm
            && vm.SelectedTab?.RightPane.SelectedFile is FileSystemModel file)
        {
            vm.RightCtxOpenCommand.Execute(file);
        }
    }

    private void RightCtxCopyToLeft_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilePageViewModel vm
            && vm.SelectedTab?.RightPane.SelectedFile is FileSystemModel file)
        {
            vm.RightCtxCopyToLeftCommand.Execute(file);
        }
    }

    private void RightCtxDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilePageViewModel vm
            && vm.SelectedTab?.RightPane.SelectedFile is FileSystemModel file)
        {
            vm.RightCtxDeleteCommand.Execute(file);
        }
    }

    // ---- 拖拽支持 ----

    private async void LeftItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 只响应左键按下，右键留给 ContextFlyout
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        if (sender is Control { DataContext: FileSystemModel file } && file.Name != "..")
        {
            var dragData = new DataObject();
            dragData.Set("FileItem", file);
            dragData.Set("SourcePane", "Left");
            await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy);
        }
    }

    private async void RightItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 只响应左键按下，右键留给 ContextFlyout
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        if (sender is Control { DataContext: FileSystemModel file } && file.Name != "..")
        {
            var dragData = new DataObject();
            dragData.Set("FileItem", file);
            dragData.Set("SourcePane", "Right");
            await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;

        if (!e.Data.Contains("FileItem") || !e.Data.Contains("SourcePane"))
            return;

        var sourcePane = e.Data.Get("SourcePane") as string;

        // 判断目标是否是另一个面板的 ListBox
        var targetListBox = (e.Source as Control)?.FindAncestorOfType<ListBox>();
        if (targetListBox == null) return;

        var targetPaneName = GetPaneName(targetListBox);
        if (targetPaneName != null && targetPaneName != sourcePane)
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("FileItem") || !e.Data.Contains("SourcePane"))
            return;

        var file = e.Data.Get("FileItem") as FileSystemModel;
        var sourcePane = e.Data.Get("SourcePane") as string;

        if (file == null || sourcePane == null || DataContext is not FilePageViewModel vm)
            return;

        var targetListBox = (e.Source as Control)?.FindAncestorOfType<ListBox>();
        if (targetListBox == null) return;

        var targetPaneName = GetPaneName(targetListBox);
        if (targetPaneName == null || targetPaneName == sourcePane) return;

        // 从左拖到右 = TransferToRight，从右拖到左 = TransferToLeft
        if (sourcePane == "Left" && targetPaneName == "Right")
        {
            vm.LeftCtxCopyToRightCommand.Execute(file);
        }
        else if (sourcePane == "Right" && targetPaneName == "Left")
        {
            vm.RightCtxCopyToLeftCommand.Execute(file);
        }
    }

    /// <summary>
    /// 根据 ListBox 在可视化树中的位置判断它属于左侧还是右侧面板
    /// </summary>
    private static string? GetPaneName(ListBox listBox)
    {
        // 向上查找 DockPanel，判断是 Grid.Column=0（左）还是 1（右）
        var dockPanel = listBox.FindAncestorOfType<DockPanel>();
        if (dockPanel != null)
        {
            var column = Grid.GetColumn(dockPanel);
            return column == 0 ? "Left" : "Right";
        }
        return null;
    }
}
