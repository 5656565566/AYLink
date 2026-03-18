using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AYLink.Core.Models;
using AYLink.Desktop.ViewModels.Pages;
using System;
using System.Collections.Generic;

namespace AYLink.Desktop.Views.Pages;

/// <summary>
/// 文件拖拽数据传输对象 - 实现 IDataTransfer 接口用于进程内拖拽
/// Avalonia 11.3 中 DataObject 已过时 新的 DataTransfer 不支持任意对象的 Set/Get
/// 需要自定义 IDataTransfer 实现来传递进程内数据
/// </summary>
internal sealed class FileDragDataTransfer : IDataTransfer
{
    public FileSystemModel? File { get; init; }
    public string? SourcePane { get; init; }

    IReadOnlyList<DataFormat> IDataTransfer.Formats => [];
    IReadOnlyList<IDataTransferItem> IDataTransfer.Items => [];
    void IDisposable.Dispose() { }
}

public partial class FilePage : UserControl
{
    public FilePage()
    {
        InitializeComponent();

        // 注册拖拽路由事件（冒泡到 UserControl 级别处理）
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // 双击事件

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

    // 左侧面板右键菜单

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

    // 右侧面板右键菜单

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

    // 拖拽支持

    private async void LeftItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 只响应左键按下 右键留给 ContextFlyout
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        if (sender is Control { DataContext: FileSystemModel file } && file.Name != "..")
        {
            var dragData = new FileDragDataTransfer
            {
                File = file,
                SourcePane = "Left"
            };
            await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Copy);
        }
    }

    private async void RightItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 只响应左键按下 右键留给 ContextFlyout
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        if (sender is Control { DataContext: FileSystemModel file } && file.Name != "..")
        {
            var dragData = new FileDragDataTransfer
            {
                File = file,
                SourcePane = "Right"
            };
            await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Copy);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;

        if (e.DataTransfer is not FileDragDataTransfer dragData)
            return;

        if (dragData.File == null || dragData.SourcePane == null)
            return;

        // 判断目标是否是另一个面板的 ListBox
        var targetListBox = (e.Source as Control)?.FindAncestorOfType<ListBox>();
        if (targetListBox == null) return;

        var targetPaneName = GetPaneName(targetListBox);
        if (targetPaneName != null && targetPaneName != dragData.SourcePane)
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer is not FileDragDataTransfer dragData)
            return;

        var file = dragData.File;
        var sourcePane = dragData.SourcePane;

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
    /// <param name="listBox"></param>
    /// <returns></returns>
    private static string? GetPaneName(ListBox listBox)
    {
        // 向上查找 DockPanel 判断是 Grid.Column=0（左）还是 1（右）
        var dockPanel = listBox.FindAncestorOfType<DockPanel>();
        if (dockPanel != null)
        {
            var column = Grid.GetColumn(dockPanel);
            return column == 0 ? "Left" : "Right";
        }
        return null;
    }
}
