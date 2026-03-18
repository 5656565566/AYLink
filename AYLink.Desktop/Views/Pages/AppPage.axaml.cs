using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AYLink.Core.Models;
using AYLink.Desktop.ViewModels.Pages;
using FluentAvalonia.UI.Controls;
using System.Collections.Generic;
using System.Linq;

namespace AYLink.Desktop.Views.Pages;

public partial class AppPage : UserControl
{
    public AppPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 搜索框回车键处理
    /// </summary>
    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is TextBox textBox && textBox.DataContext is AppTabViewModel tabVm)
            {
                tabVm.SearchCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// 安装 APK 按钮点击 - 打开文件选择器
    /// </summary>
    private async void InstallAppBtn_Click(object? sender, RoutedEventArgs e)
    {
        // 获取当前标签页的 ViewModel
        AppTabViewModel? tabVm = null;

        if (sender is Control control)
        {
            tabVm = control.DataContext as AppTabViewModel;
        }

        var localizer = Services.Localization.LocalizationManager.Instance;
        if (tabVm?.Device == null)
        {
            Services.DialogHelper.ShowToast(
                localizer.GetString("Dialog.Tip", "提示"),
                localizer.GetString("AppPage.SelectDeviceFirst", "请先选择设备"),
                InfoBarSeverity.Warning);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizer.GetString("AppPage.SelectApkTitle", "选择要安装的 APK 文件"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(localizer.GetString("AppPage.ApkFiles", "APK 文件 (*.apk)")) { Patterns = ["*.apk"] },
                new FilePickerFileType(localizer.GetString("AppPage.AllFiles", "所有文件 (*.*)")) { Patterns = ["*"] },
            ]
        });

        if (files.Count > 0)
        {
            var filePaths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Cast<string>()
                .ToList();

            if (filePaths.Count > 0)
            {
                await tabVm.InstallApkCommand.ExecuteAsync(filePaths);
            }
        }
    }

    /// <summary>
    /// 右键菜单 - 卸载应用
    /// </summary>
    private void CtxUninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppPageViewModel vm && vm.SelectedTab?.SelectedApp is AppInfo app)
        {
            vm.UninstallAppCommand.Execute(app);
        }
    }

    private void CtxLaunch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppPageViewModel vm && vm.SelectedTab?.SelectedApp is AppInfo app)
        {
            vm.LaunchAppCommand.Execute(app);
        }
    }

    private void CtxNewLaunch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppPageViewModel vm && vm.SelectedTab?.SelectedApp is AppInfo app)
        {
            vm.LaunchAppNewDisplayCommand.Execute(app);
        }
    }

    private void CtxDownload_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: 实现下载 APK 到本地
    }

    private void CtxCopyPackage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppPageViewModel vm && vm.SelectedTab?.SelectedApp is AppInfo app)
        {
            vm.CopyPackageNameCommand.Execute(app);
        }
    }

    private void CtxAppInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppPageViewModel vm && vm.SelectedTab?.SelectedApp is AppInfo app)
        {
            vm.OpenAppInfoCommand.Execute(app);
        }
    }
}
