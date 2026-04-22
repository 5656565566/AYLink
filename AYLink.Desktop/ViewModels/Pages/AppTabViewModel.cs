using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Notifications;
using AYLink.Desktop.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 应用管理标签页 ViewModel - 每个设备对应一个标签页
/// </summary>
public partial class AppTabViewModel : TabItemViewModelBase
{
    /// <summary>
    /// 当前显示的应用列表（经过搜索过滤）
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<AppInfo> Apps { get; set; } = [];

    /// <summary>
    /// 选中的应用
    /// </summary>
    [ObservableProperty]
    public partial AppInfo? SelectedApp { get; set; }

    /// <summary>
    /// 搜索关键词
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// 是否正在加载
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// 是否有应用数据（控制空状态提示）
    /// </summary>
    [ObservableProperty]
    public partial bool HasApps { get; set; }

    /// <summary>
    /// 应用数量文本
    /// </summary>
    [ObservableProperty]
    public partial string AppCountText { get; set; } = string.Empty;

    /// <summary>
    /// 全应用列表
    /// </summary>
    private readonly List<AppInfo> _masterAppList = [];
    private readonly NotificationService _notifications = NotificationService.Instance;

    public AppTabViewModel(DeviceModel device)
    {
        Device = device;
        Title = device.Name;
        _ = LoadAppsAsync();
    }

    /// <summary>
    /// 加载应用列表
    /// </summary>
    [RelayCommand]
    private async Task LoadAppsAsync()
    {
        if (Device == null) return;

        IsLoading = true;
        StatusMessage = Services.Localization.LocalizationManager.Instance.GetString("AppTab.LoadingApps", "正在加载应用列表...");
        AppCountText = string.Empty;
        HasApps = false;

        try
        {
            var appList = await Task.Run(() =>
            {
                ScrcpyTool tool = ScrcpyService.Instance.Tool;
                return tool.GetAppInfos(Device);
            });

            _masterAppList.Clear();
            _masterAppList.AddRange(appList);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            StatusMessage = string.Format(localizer.GetString("AppTab.LoadFailedStatus", "加载失败: {0}"), ex.Message);
            _notifications.ShowError(
                localizer.GetString("AppTab.LoadFailedTitle", "加载失败"),
                string.Format(localizer.GetString("AppTab.LoadFailedMessage", "获取应用列表失败: {0}"), ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 搜索应用
    /// </summary>
    [RelayCommand]
    private void Search()
    {
        ApplyFilter();
    }

    /// <summary>
    /// 应用搜索过滤
    /// </summary>
    private void ApplyFilter()
    {
        var searchText = SearchText?.Trim() ?? string.Empty;

        Apps.Clear();

        if (string.IsNullOrEmpty(searchText))
        {
            foreach (var app in _masterAppList)
            {
                Apps.Add(app);
            }
        }
        else
        {
            var filtered = _masterAppList.Where(app =>
                app.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                app.PackageName.Contains(searchText, StringComparison.OrdinalIgnoreCase));

            foreach (var app in filtered)
            {
                Apps.Add(app);
            }
        }

        HasApps = Apps.Count > 0;
        // 左侧提示：搜索时如果无结果才提示
        var localizer = Services.Localization.LocalizationManager.Instance;
        StatusMessage = (!HasApps && !string.IsNullOrEmpty(searchText)) ? localizer.GetString("AppTab.NoMatchingApps", "未找到匹配的应用") : string.Empty;
        // 右侧：有应用时显示数量
        AppCountText = HasApps ? string.Format(localizer.GetString("AppTab.AppCount", "共 {0} 个应用"), Apps.Count) : string.Empty;
    }

    /// <summary>
    /// 安装 APK（由 View 调用 传入文件路径列表）
    /// </summary>
    [RelayCommand]
    private async Task InstallApkAsync(IReadOnlyList<string>? filePaths)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        if (Device == null)
        {
            _notifications.ShowWarning(
                localizer.GetString("Dialog.Tip", "提示"),
                localizer.GetString("AppTab.SelectDeviceFirst", "请先选择设备"));
            return;
        }

        if (filePaths == null || filePaths.Count == 0) return;

        string title = localizer.GetString("AppTab.InstallAppTitle", "安装应用");
        string message = localizer.GetString("AppTab.PreparingInstall", "准备安装...");

        var managedTask = TaskService.Instance.StartTask(new ManagedTaskOptions
        {
            Title = title,
            Description = message,
            Source = localizer.GetString("TaskPage.DefaultSource", "通用任务"),
            IsIndeterminate = false,
            ShowProgress = true
        });

        var dialog = new Views.Dialogs.ProgressDialog();
        _ = dialog.ShowAsync(title, message, isIndeterminate: false);

        try
        {
            await Task.Run(async () =>
            {
                foreach (var filePath in filePaths)
                {
                    if (string.IsNullOrEmpty(filePath)) continue;

                    var fileName = Path.GetFileName(filePath);

                    using var stream = File.OpenRead(filePath);

                    void callback(InstallProgressEventArgs p)
                    {
                        string msg = string.Empty;
                        if (p.State == PackageInstallProgressState.Uploading)
                        {
                            msg = string.Format(localizer.GetString("AppTab.Uploading", "正在上传: {0}"), fileName);
                        }
                        else if (p.State == PackageInstallProgressState.Installing)
                        {
                            msg = string.Format(localizer.GetString("AppTab.Installing", "正在安装: {0}"), fileName);
                        }

                        if (!string.IsNullOrEmpty(msg))
                        {
                            dialog.UpdateProgress(p.UploadProgress, msg);
                            TaskService.Instance.UpdateTask(managedTask, p.UploadProgress, msg);
                        }
                    }

                    await AdbClient.Instance.InstallAsync(
                        Device.DeviceData,
                        stream,
                        callback,
                        CancellationToken.None,
                        "-r"
                    );
                }
            });

            dialog.Hide();
            TaskService.Instance.CompleteTask(managedTask, localizer.GetString("AppTab.InstallSuccessMessage", "APK 安装完成"));
            _notifications.ShowSuccess(
                localizer.GetString("AppTab.InstallSuccessTitle", "安装成功"),
                localizer.GetString("AppTab.InstallSuccessMessage", "APK 安装完成"));

            // 刷新应用列表
            await LoadAppsAsync();
        }
        catch (Exception ex)
        {
            dialog.Hide();
            TaskService.Instance.FailTask(managedTask, string.Format(localizer.GetString("AppTab.InstallFailedMessage", "安装应用时发生错误: {0}"), ex.Message));
            _notifications.ShowError(
                localizer.GetString("AppTab.InstallFailedTitle", "安装失败"),
                string.Format(localizer.GetString("AppTab.InstallFailedMessage", "安装应用时发生错误: {0}"), ex.Message));
        }
    }

    /// <summary>
    /// 卸载选中的应用
    /// </summary>
    [RelayCommand]
    private async Task UninstallAppAsync(AppInfo? app)
    {
        if (Device == null || app == null) return;

        var localizer = Services.Localization.LocalizationManager.Instance;
        var result = await DialogService.ShowMessageAsync(
            localizer.GetString("AppTab.ConfirmUninstallTitle", "确认卸载"),
            string.Format(localizer.GetString("AppTab.ConfirmUninstallMessage", "确定要卸载 {0} ({1}) 吗？"), app.Name, app.PackageName),
            localizer.GetString("AppTab.UninstallButton", "卸载"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary) return;

        string title = localizer.GetString("AppTab.UninstallAppTitle", "卸载应用");
        string message = string.Format(localizer.GetString("AppTab.Uninstalling", "正在卸载 {0}..."), app.Name);

        var managedTask = TaskService.Instance.StartTask(new ManagedTaskOptions
        {
            Title = title,
            Description = message,
            Source = localizer.GetString("TaskPage.DefaultSource", "通用任务"),
            IsIndeterminate = true,
            ShowProgress = true
        });

        var dialog = new Views.Dialogs.ProgressDialog();
        _ = dialog.ShowAsync(title, message, isIndeterminate: true);

        try
        {
            await Task.Run(async () =>
            {
                await AdbClient.Instance.UninstallAsync(
                    Device.DeviceData,
                    app.PackageName,
                    CancellationToken.None);
            });

            dialog.Hide();
            string successMsg = string.Format(localizer.GetString("AppTab.UninstallSuccessMessage", "{0} 已卸载"), app.Name);
            TaskService.Instance.CompleteTask(managedTask, successMsg);
            _notifications.ShowSuccess(
                localizer.GetString("AppTab.UninstallSuccessTitle", "卸载成功"),
                successMsg);

            // 从列表中移除
            _masterAppList.Remove(app);
            Apps.Remove(app);
            HasApps = Apps.Count > 0;
            StatusMessage = HasApps ? string.Format(localizer.GetString("AppTab.AppCount", "共 {0} 个应用"), Apps.Count) : localizer.GetString("AppTab.NoAppsFound", "未找到应用");
        }
        catch (Exception ex)
        {
            dialog.Hide();
            TaskService.Instance.FailTask(managedTask, string.Format(localizer.GetString("AppTab.UninstallFailedMessage", "卸载应用时发生错误: {0}"), ex.Message));
            _notifications.ShowError(
                localizer.GetString("AppTab.UninstallFailedTitle", "卸载失败"),
                string.Format(localizer.GetString("AppTab.UninstallFailedMessage", "卸载应用时发生错误: {0}"), ex.Message));
        }
    }

    /// <summary>
    /// 启动应用（通过 monkey 命令）
    /// </summary>
    [RelayCommand]
    private async Task LaunchAppAsync(AppInfo? app)
    {
        if (Device == null || app == null) return;
        var localizer = Services.Localization.LocalizationManager.Instance;

        try
        {
            await Task.Run(() =>
            {
                var receiver = new ConsoleOutputReceiver();
                AdbClient.Instance.ExecuteRemoteCommand(
                    $"am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER {app.PackageName}",
                    Device.DeviceData,
                    receiver);
            });
            _notifications.ShowSuccess(
                localizer.GetString("AppTab.LaunchSuccessTitle", "启动成功"),
                string.Format(localizer.GetString("AppTab.LaunchSuccessMessage", "{0} 已启动"), app.Name));
        }
        catch (Exception ex)
        {
            _notifications.ShowError(
                localizer.GetString("AppTab.LaunchFailedTitle", "启动失败"),
                string.Format(localizer.GetString("AppTab.LaunchFailedMessage", "启动应用失败: {0}"), ex.Message));
        }
    }

    /// <summary>
    /// 在新建屏幕中启动应用（导航到投屏页并指定应用）
    /// </summary>
    [RelayCommand]
    private void LaunchAppNewDisplay(AppInfo? app)
    {
        if (Device == null || app == null) return;

        NavigationService.Instance.NavigateTo("Screen", new ScreenNavigationArgs
        {
            Device = Device,
            AppPackageName = app.PackageName,
            AppDisplayName = app.Name
        });
    }

    /// <summary>
    /// 复制包名到剪贴板
    /// </summary>
    [RelayCommand]
    private async Task CopyPackageNameAsync(AppInfo? app)
    {
        if (app == null) return;

        var localizer = Services.Localization.LocalizationManager.Instance;

        try
        {
            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard
                : null;

            if (clipboard != null)
            {
                await clipboard.SetTextAsync(app.PackageName);
                _notifications.ShowSuccess(
                    localizer.GetString("AppTab.CopySuccessTitle", "已复制"),
                    string.Format(localizer.GetString("AppTab.CopySuccessMessage", "包名 {0} 已复制到剪贴板"), app.PackageName));
            }
        }
        catch (Exception ex)
        {
            _notifications.ShowError(
                localizer.GetString("AppTab.CopyFailedTitle", "复制失败"),
                string.Format(localizer.GetString("AppTab.CopyFailedMessage", "复制包名失败: {0}"), ex.Message));
        }
    }

    /// <summary>
    /// 打开应用信息（通过 ADB 打开系统设置中的应用详情页）
    /// </summary>
    [RelayCommand]
    private async Task OpenAppInfoAsync(AppInfo? app)
    {
        if (Device == null || app == null) return;

        var localizer = Services.Localization.LocalizationManager.Instance;

        try
        {
            await Task.Run(() =>
            {
                var receiver = new ConsoleOutputReceiver();
                AdbClient.Instance.ExecuteRemoteCommand(
                    $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d package:{app.PackageName}",
                    Device.DeviceData,
                    receiver);
            });

            _notifications.ShowInfo(
                localizer.GetString("AppTab.AppInfoOpenedTitle", "应用信息"),
                string.Format(localizer.GetString("AppTab.AppInfoOpenedMessage", "已在设备上打开 {0} 的应用信息"), app.Name));
        }
        catch (Exception ex)
        {
            _notifications.ShowError(
                localizer.GetString("AppTab.AppInfoFailedTitle", "打开失败"),
                string.Format(localizer.GetString("AppTab.AppInfoFailedMessage", "打开应用信息失败: {0}"), ex.Message));
        }
    }
}
