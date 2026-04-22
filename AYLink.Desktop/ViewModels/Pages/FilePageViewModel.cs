using AYLink.Core.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 文件管理页 ViewModel
/// </summary>
public partial class FilePageViewModel : TabbedPageViewModelBase<FileTabViewModel>
{
    public override string PageKey => "File";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("FilePage.Title", "文件管理");
    public override string EmptyStateIcon => "Document";
    public override string EmptyStateTitle => Services.Localization.LocalizationManager.Instance.GetString("FilePage.EmptyStateTitle", "无标签页");
    public override string EmptyStateDescription => Services.Localization.LocalizationManager.Instance.GetString("FilePage.EmptyStateDescription", "点击 + 创建新标签页");
    public override bool IsAddTabButtonVisible => true;

    public FilePageViewModel()
    {
        // 默认添加一个本地标签页，且不可关闭
        var defaultTab = new FileTabViewModel
        {
            IsClosable = false
        };
        RegisterTab(defaultTab);
    }

    protected override FileTabViewModel CreateTab(DeviceModel device) => new(device);

    /// <summary>
    /// 拦截关闭：至少保留一个标签页
    /// </summary>
    protected override bool OnTabClosing(FileTabViewModel tab)
    {
        if (Tabs.Count <= 1)
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            Services.Notifications.NotificationService.Instance.ShowInfo(
                localizer.GetString("FilePage.CannotCloseTitle", "无法关闭"),
                localizer.GetString("FilePage.CannotCloseMessage", "至少要保留一个标签页"));
            return false; // 阻止关闭
        }
        return true;
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
        tab?.CloseTabCommand.Execute(null);
    }

    // 右键菜单命令

    [RelayCommand]
    private void LeftCtxOpen(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.LeftPane.NavigateToCommand.Execute(file);
        }
    }

    [RelayCommand]
    private void LeftCtxCopyToRight(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.TransferToRightCommand.Execute(file);
        }
    }

    [RelayCommand]
    private void LeftCtxDelete(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.LeftPane.DeleteFileCommand.Execute(file);
        }
    }

    [RelayCommand]
    private void RightCtxOpen(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.RightPane.NavigateToCommand.Execute(file);
        }
    }

    [RelayCommand]
    private void RightCtxCopyToLeft(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.TransferToLeftCommand.Execute(file);
        }
    }

    [RelayCommand]
    private void RightCtxDelete(FileSystemModel? file)
    {
        if (file != null && SelectedTab != null)
        {
            SelectedTab.RightPane.DeleteFileCommand.Execute(file);
        }
    }
}
