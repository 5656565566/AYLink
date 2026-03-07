using AYLink.Core.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 文件管理页 ViewModel
/// </summary>
public partial class FilePageViewModel : PageViewModelBase
{
    public override string PageKey => "File";
    public override string Title => "文件管理";

    [ObservableProperty]
    private ObservableCollection<FileTabViewModel> _tabs = new();

    [ObservableProperty]
    private FileTabViewModel? _selectedTab;

    public FilePageViewModel()
    {
        // 默认添加一个本地标签页
        AddNewTab();
    }

    [RelayCommand]
    private void AddNewTab(DeviceModel? device = null)
    {
        var newTab = new FileTabViewModel(device);
        newTab.OnCloseRequested += Tab_OnCloseRequested;
        Tabs.Add(newTab);
        SelectedTab = newTab;
    }

    private void Tab_OnCloseRequested(FileTabViewModel tab)
    {
        tab.OnCloseRequested -= Tab_OnCloseRequested;
        Tabs.Remove(tab);
        
        if (Tabs.Count == 0)
        {
            DialogHelper.ShowToast("无法关闭", "至少要保留一个标签页", InfoBarSeverity.Informational);
            AddNewTab();
        }
        else if (SelectedTab == null)
        {
            SelectedTab = Tabs.LastOrDefault();
        }
    }

    /// <summary>
    /// 左侧面板项目双击 - 导航到该文件/文件夹
    /// </summary>
    [RelayCommand]
    private void LeftItemDoubleTapped(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.LeftPane.NavigateToCommand.Execute(file);
        }
    }

    /// <summary>
    /// 右侧面板项目双击 - 导航到该文件/文件夹
    /// </summary>
    [RelayCommand]
    private void RightItemDoubleTapped(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.RightPane.NavigateToCommand.Execute(file);
        }
    }

    /// <summary>
    /// 关闭标签页
    /// </summary>
    [RelayCommand]
    private void CloseTab(FileTabViewModel? tab)
    {
        if (tab != null)
        {
            tab.CloseTabCommand.Execute(null);
        }
    }

    // 右键菜单命令

    /// <summary>
    /// 左侧面板 - 打开（右键菜单）
    /// </summary>
    [RelayCommand]
    private void LeftCtxOpen(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.LeftPane.NavigateToCommand.Execute(file);
        }
    }

    /// <summary>
    /// 左侧面板 - 复制到右侧（右键菜单）
    /// </summary>
    [RelayCommand]
    private void LeftCtxCopyToRight(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.TransferToRightCommand.Execute(file);
        }
    }

    /// <summary>
    /// 左侧面板 - 删除（右键菜单）
    /// </summary>
    [RelayCommand]
    private void LeftCtxDelete(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.LeftPane.DeleteFileCommand.Execute(file);
        }
    }

    /// <summary>
    /// 右侧面板 - 打开（右键菜单）
    /// </summary>
    [RelayCommand]
    private void RightCtxOpen(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.RightPane.NavigateToCommand.Execute(file);
        }
    }

    /// <summary>
    /// 右侧面板 - 复制到左侧（右键菜单）
    /// </summary>
    [RelayCommand]
    private void RightCtxCopyToLeft(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.TransferToLeftCommand.Execute(file);
        }
    }

    /// <summary>
    /// 右侧面板 - 删除（右键菜单）
    /// </summary>
    [RelayCommand]
    private void RightCtxDelete(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.RightPane.DeleteFileCommand.Execute(file);
        }
    }

    public override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);
        
        if (parameter is DeviceModel device)
        {
            // 检查是否已经有该设备的标签页
            var existingTab = Tabs.FirstOrDefault(t => t.Device?.Serial == device.Serial);
            if (existingTab != null)
            {
                SelectedTab = existingTab;
            }
            else
            {
                AddNewTab(device);
            }
        }
    }
}
