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
    // ﻅﭨﺵﺿﮌﺝﺭﺕﺹ۰ﭦﮩﭖﺥﺛﻕﺗﻳ
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
    /// ﺯﺹﺯﺊ
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
            await DialogHelper.MessageShowAsync("ﺳﻐﺭﻟﺎﺕ", "ﮄﻣﺵﺫﮄﺍﺱﻱ ﮌﻉﺻﺏ ﺹ۰ﺿﮦﺻﭨﺕﺉﺭﻟﺎﺕ");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "ﮄﻣﺹ۰ﺿﮦﺻﭨﺕﺉﭨﮨﭘﻓﺕﺉ APK ﺳﺥﺙﹼ",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("APK (*.apk)") { Patterns = ["*.apk"] },
                new FilePickerFileType("ﺯﻱﺽﺷﺳﺥﺙﹼ (*.*)") { Patterns = ["*"] },
            ]
        });

        if (files.Any())
        {
            DialogHelper.GetProgressShow(
                "ﺽ۵ﺽﺣﺍﺎﻉﺍ",
                "ﻉﺙﺎﺕﺍﺎﻉﺍ...",
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
                                DialogHelper.UpdateProgressMessage($"ﻁﮮﺿﻌﺭﺵﺑ، {fileName}");
                                DialogHelper.UpdateProgressValue(p.UploadProgress);
                            }

                            if (p.State == PackageInstallProgressState.Installing)
                            {
                                DialogHelper.UpdateProgressMessage($"ﻁﮮﺿﻌﺍﺎﻉﺍ");
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
                    await DialogHelper.MessageShowAsync("ﺍﺎﻉﺍﮌ۶ﺍﻎ", $"ﺽ۵ﺽﺣﺍﺎﻉﺍﺗﮮﺏﮊﻅﺷﺓ۱ﺭﻲﺑﻥﺳﮩ: {ex.Message}");
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
            "ﺽ۵ﺽﺣﭨﮦﺫ۰",
            "ﭨﮦﺫ۰ﻅﺷ...",
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