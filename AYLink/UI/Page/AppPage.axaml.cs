using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AYLink.ADB;
using AYLink.Scrcpy;
using AYLink.UI.Themes;
using AYLink.UIModel;
using AYLink.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.UI;

public partial class AppPage : UserControl
{
    public event Action<DeviceModel, string, string>? OnAppStart;
    // 只显示筛选后的结果
    private readonly ObservableCollection<AppInfo> _appList = [];
    private readonly List<AppInfo> _masterAppList = [];
    private DeviceModel? _deviceModel;
    private ScrcpyTool? scrcpyTool;

    public AppPage()
    {
        InitializeComponent();

        AppDataGrid.ItemsSource = _appList;

        if (AppDataGrid.ContextFlyout is not MenuFlyout flyout) return;

        SearchAppBtn.Click += SearchAppBtn_Click;
        SearchBox.KeyDown += SearchBox_KeyDown;

        CtxLaunch.Click += CtxLaunch_Click;
        CtxNewLaunch.Click += CtxNewLaunch_Click;

        InstallAppBtn.Click += InstallAppBtn_Click;

        flyout.Opening += (s, e) =>
        {
            bool hasSelection = AppDataGrid.SelectedItem != null;

            var launch = flyout.Items.OfType<MenuItem>().First(x => x.Name == "CtxLaunch");
            var newLaunch = flyout.Items.OfType<MenuItem>().First(x => x.Name == "CtxNewLaunch");
            var download = flyout.Items.OfType<MenuItem>().First(x => x.Name == "CtxDownload");
            var uninstall = flyout.Items.OfType<MenuItem>().First(x => x.Name == "CtxUninstall");
            var copyPkg = flyout.Items.OfType<MenuItem>().First(x => x.Name == "CtxCopyPackage");
            var info = flyout.Items.OfType<MenuItem>().First(x => x.Name == "CtxAppInfo");
            var tip = flyout.Items.OfType<MenuItem>().First(x => x.Name == "CtxTip");

            launch.IsEnabled = hasSelection;
            newLaunch.IsEnabled = hasSelection;
            download.IsEnabled = hasSelection;
            uninstall.IsEnabled = hasSelection;
            copyPkg.IsEnabled = hasSelection;
            info.IsEnabled = hasSelection;
            tip.IsVisible = !hasSelection;
        };
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformSearch();
        }
    }

    private void SearchAppBtn_Click(object? sender, RoutedEventArgs e)
    {
        PerformSearch();
    }

    /// <summary>
    /// 搜索
    /// </summary>
    private void PerformSearch()
    {
        var searchText = SearchBox.Text?.Trim() ?? string.Empty;

        _appList.Clear();

        if (string.IsNullOrEmpty(searchText))
        {
            foreach (var app in _masterAppList)
            {
                _appList.Add(app);
            }
        }
        else
        {
            var filteredList = _masterAppList.Where(app =>
                app.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                app.PackageName.Contains(searchText, StringComparison.OrdinalIgnoreCase));

            foreach (var app in filteredList)
            {
                _appList.Add(app);
            }
        }
    }

    private async void InstallAppBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_deviceModel == null)
        {
            await DialogHelper.MessageShowAsync("无设备", "请先前往 首页 选择一个设备");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "请选择一个或多个 APK 文件",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("APK (*.apk)") { Patterns = ["*.apk"] },
                new FilePickerFileType("所有文件 (*.*)") { Patterns = ["*"] },
            ]
        });

        if (files.Any())
        {
            DialogHelper.GetProgressShow(
                "应用安装",
                "准备安装...",
                showProgressBar: true
            );
            DialogHelper.ShowProgress();

            await Task.Run(async () =>
            {
                try
                {
                    foreach (var file in files)
                    {
                        var selectedFilePath = file.TryGetLocalPath();
                        if (string.IsNullOrEmpty(selectedFilePath))
                        {
                            continue;
                        }

                        var fileName = Path.GetFileName(selectedFilePath);

                        using var stream = File.OpenRead(selectedFilePath);

                        void callback(InstallProgressEventArgs p)
                        {
                            if (p.State == PackageInstallProgressState.Uploading)
                            {
                                DialogHelper.UpdateProgressMessage($"正在上传 {fileName}");
                                DialogHelper.UpdateProgressValue(p.UploadProgress);
                            }

                            if (p.State == PackageInstallProgressState.Installing)
                            {
                                DialogHelper.UpdateProgressMessage($"正在安装");
                                DialogHelper.UpdateProgressValue(p.UploadProgress);
                            }
                        }

                        await AdbClient.Instance.InstallAsync(
                            _deviceModel.DeviceData,
                            stream,
                            callback,
                            CancellationToken.None,
                            "-r"
                        );
                    }

                    DialogHelper.CloseProgress();
                }
                catch (Exception ex)
                {
                    DialogHelper.CloseProgress();
                    await DialogHelper.MessageShowAsync("安装失败", $"应用安装过程中发生错误: {ex.Message}");
                }
            });
        }
    }

    private void CtxNewLaunch_Click(object? sender, RoutedEventArgs e)
    {
        if (_deviceModel == null) return;

        if (AppDataGrid.SelectedItem is AppInfo selected)
        {
            string name = selected.Name;
            string package = selected.PackageName;

            OnAppStart?.Invoke(_deviceModel, name, package);
        }
    }

    private void CtxLaunch_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    public void SelectDevice(DeviceModel deviceModel)
    {
        _deviceModel = deviceModel;
        scrcpyTool = new ScrcpyTool(deviceModel);
        LoadAppData();
    }

    private void LoadAppData()
    {
        DialogHelper.GetProgressShow(
            "应用获取",
            "获取中...",
            showProgressBar: false
            );

        DialogHelper.ShowProgress();

        _ = Task.Run(() =>
        {
            List<AppInfo>? appList = scrcpyTool?.GetAppInfos();

            Dispatcher.UIThread.Post(() =>
            {
                _masterAppList.Clear();
                _appList.Clear();

                if (appList != null)
                {
                    foreach (var app in appList)
                    {
                        _masterAppList.Add(app);
                        _appList.Add(app);
                    }
                }

                AppListContainer.IsVisible = (_appList.Count > 0);
                Tip.IsVisible = !(_appList.Count > 0);

                DialogHelper.CloseProgress();
            });
        });
    }
}