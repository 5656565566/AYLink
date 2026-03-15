using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
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
    private ObservableCollection<AppInfo> _apps = [];

    /// <summary>
    /// 选中的应用
    /// </summary>
    [ObservableProperty]
    private AppInfo? _selectedApp;

    /// <summary>
    /// 搜索关键词
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 是否正在加载
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// 是否有应用数据（控制空状态提示）
    /// </summary>
    [ObservableProperty]
    private bool _hasApps;

    /// <summary>
    /// 应用数量文本
    /// </summary>
    [ObservableProperty]
    private string _appCountText = string.Empty;

    /// <summary>
    /// 全应用列表
    /// </summary>
    private readonly List<AppInfo> _masterAppList = [];

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
        StatusMessage = "正在加载应用列表...";
        AppCountText = string.Empty;
        HasApps = false;

        try
        {
            var appList = await Task.Run(() =>
            {
                var tool = new ScrcpyTool(Device, "Scrcpy/scrcpy-server");
                return tool.GetAppInfos();
            });

            _masterAppList.Clear();
            _masterAppList.AddRange(appList);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
            DialogHelper.ShowToast("加载失败", $"获取应用列表失败: {ex.Message}", InfoBarSeverity.Error);
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
        StatusMessage = (!HasApps && !string.IsNullOrEmpty(searchText)) ? "未找到匹配的应用" : string.Empty;
        // 右侧：有应用时显示数量
        AppCountText = HasApps ? $"共 {Apps.Count} 个应用" : string.Empty;
    }

    /// <summary>
    /// 安装 APK（由 View 调用，传入文件路径列表）
    /// </summary>
    [RelayCommand]
    private async Task InstallApkAsync(IReadOnlyList<string>? filePaths)
    {
        if (Device == null)
        {
            DialogHelper.ShowToast("提示", "请先选择设备", InfoBarSeverity.Warning);
            return;
        }

        if (filePaths == null || filePaths.Count == 0) return;

        DialogHelper.ShowProgress("安装应用", "准备安装...", isBlocking: true, isIndeterminate: false);

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
                        if (p.State == PackageInstallProgressState.Uploading)
                        {
                            DialogHelper.UpdateProgress(p.UploadProgress, $"正在上传: {fileName}");
                        }

                        if (p.State == PackageInstallProgressState.Installing)
                        {
                            DialogHelper.UpdateProgress(p.UploadProgress, $"正在安装: {fileName}");
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

            DialogHelper.CloseProgress();
            DialogHelper.ShowToast("安装成功", "APK 安装完成", InfoBarSeverity.Success);

            // 刷新应用列表
            await LoadAppsAsync();
        }
        catch (Exception ex)
        {
            DialogHelper.CloseProgress();
            await DialogHelper.ShowMessageAsync("安装失败", $"安装应用时发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 卸载选中的应用
    /// </summary>
    [RelayCommand]
    private async Task UninstallAppAsync(AppInfo? app)
    {
        if (Device == null || app == null) return;

        var result = await DialogHelper.ShowMessageAsync(
            "确认卸载",
            $"确定要卸载 {app.Name} ({app.PackageName}) 吗？",
            "卸载",
            "取消");

        if (result != ContentDialogResult.Primary) return;

        DialogHelper.ShowProgress("卸载应用", $"正在卸载 {app.Name}...", isBlocking: true);

        try
        {
            await Task.Run(async () =>
            {
                await AdbClient.Instance.UninstallAsync(
                    Device.DeviceData,
                    app.PackageName,
                    CancellationToken.None);
            });

            DialogHelper.CloseProgress();
            DialogHelper.ShowToast("卸载成功", $"{app.Name} 已卸载", InfoBarSeverity.Success);

            // 从列表中移除
            _masterAppList.Remove(app);
            Apps.Remove(app);
            HasApps = Apps.Count > 0;
            StatusMessage = HasApps ? $"共 {Apps.Count} 个应用" : "未找到应用";
        }
        catch (Exception ex)
        {
            DialogHelper.CloseProgress();
            await DialogHelper.ShowMessageAsync("卸载失败", $"卸载应用时发生错误: {ex.Message}");
        }
    }
}
