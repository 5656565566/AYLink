using AdvancedSharpAdbClient.DeviceCommands;
using AYLink.Core.ADB;
using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Notifications;
using AYLink.Desktop.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class FileTabViewModel : TabItemViewModelBase
{
    public FilePaneViewModel LeftPane { get; }
    public FilePaneViewModel RightPane { get; }
    public string? RemoteDeviceId { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftPaneVisible), nameof(IsRightPaneVisible), nameof(IsSingleLeft), nameof(IsSingleRight))]
    public partial bool IsDualPane { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftPaneVisible), nameof(IsRightPaneVisible), nameof(IsSingleLeft), nameof(IsSingleRight))]
    public partial bool IsRightActive { get; set; }

    public bool IsLeftPaneVisible => IsDualPane || !IsRightActive;
    public bool IsRightPaneVisible => IsDualPane || IsRightActive;
    public bool IsSingleLeft => !IsDualPane && !IsRightActive;
    public bool IsSingleRight => !IsDualPane && IsRightActive;

    public void ActivatePane(FilePaneViewModel pane) => IsRightActive = ReferenceEquals(pane, RightPane);

    public FileTabViewModel(DeviceModel? device = null)
    {
        LeftPane = new();
        RightPane = new();
        Device = device;
        
        if (device != null)
        {
            Title = device.Name;
            LeftPane.SelectLocalHome();
            RightPane.SelectDevice(device);
            IsRightActive = true;
        }
        else
        {
            Title = Services.Localization.LocalizationManager.Instance.GetString("FilePage.Title", "文件管理");
            LeftPane.SelectLocalHome();
            RightPane.SelectLocalHome();
        }
    }

    public FileTabViewModel(DeviceDescriptor remoteDevice, string serverId, string? initialPath = null)
    {
        LeftPane = new();
        RightPane = new();
        RemoteDeviceId = remoteDevice.Id;
        Title = remoteDevice.Name;
        LeftPane.SelectLocalHome();
        RightPane.SelectRemoteDevice(remoteDevice, serverId, initialPath);
        IsRightActive = true;
    }

    public FileTabViewModel(FileTabViewModel source)
    {
        LeftPane = new(source.LeftPane);
        RightPane = new(source.RightPane);
        Title = source.Title;
        Device = source.Device;
        RemoteDeviceId = source.RemoteDeviceId;
        IsDualPane = source.IsDualPane;
        IsRightActive = source.IsRightActive;
    }

    public FileTabViewModel CreateDirectoryTab(FilePaneViewModel pane, FileSystemModel directory)
    {
        var tab = new FileTabViewModel(this) { Title = directory.Name, IsDualPane = false };
        var target = ReferenceEquals(pane, LeftPane) ? tab.LeftPane : tab.RightPane;
        tab.ActivatePane(target);
        target.SetLocation(pane.SelectedSource, pane.GetEntryPath(directory));
        return tab;
    }

    public bool CanTransfer(FilePaneViewModel source, FileSystemModel file)
    {
        var target = ReferenceEquals(source, LeftPane) ? RightPane : LeftPane;
        return IsDualPane && source.CanOperate(file) && target.HasCurrentListing && !file.IsDirectory &&
            !(source.SelectedSource!.IsLocal && target.SelectedSource!.IsRemoteAgent) &&
            !(source.SelectedSource!.IsRemoteAgent && !target.SelectedSource!.IsLocal) &&
            !(target.SelectedSource!.IsRemoteAgent && !source.SelectedSource!.IsLocal);
    }

    public async Task TransferFilesAsync(FilePaneViewModel pane, IReadOnlyList<FileSystemModel> files)
    {
        if (files.Count == 0 || files.Any(file => !CanTransfer(pane, file))) return;
        var source = pane.SelectedSource!;
        var targetPane = ReferenceEquals(pane, LeftPane) ? RightPane : LeftPane;
        var target = targetPane.SelectedSource!;
        var directory = targetPane.CurrentPath;
        var paths = files.Select(file => (File: file, Source: pane.GetEntryPath(file), Target: targetPane.GetEntryPath(file))).ToArray();
        foreach (var entry in paths) await CopyFileAsync(source, entry.Source, target, entry.Target, entry.File);
        if (ReferenceEquals(target, targetPane.SelectedSource) && targetPane.CurrentPath == directory)
            await targetPane.LoadFilesAsync();
    }

    public async Task DownloadFilesToLocalAsync(FilePaneViewModel pane, IReadOnlyList<FileSystemModel> files, string directory)
    {
        if (files.Count == 0 || pane.SelectedSource?.IsLocal != false || files.Any(file => !pane.CanOperate(file) || file.IsDirectory)) return;
        var source = pane.SelectedSource;
        var paths = files.Select(file => (File: file, Source: pane.GetEntryPath(file), Target: System.IO.Path.Combine(directory, file.Name))).ToArray();
        foreach (var entry in paths) await CopyFileAsync(source, entry.Source, new FileSource(), entry.Target, entry.File);
    }

    /// <summary>
    /// 传输到右侧
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task TransferToRight(FileSystemModel? file)
    {
        if (file == null || file.Name == "..") return;
        await TransferFileAsync(LeftPane, RightPane, file);
    }

    /// <summary>
    /// 传输到左侧
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task TransferToLeft(FileSystemModel? file)
    {
        if (file == null || file.Name == "..") return;
        await TransferFileAsync(RightPane, LeftPane, file);
    }

    /// <summary>
    /// 传输文件核心逻辑
    /// </summary>
    private async System.Threading.Tasks.Task TransferFileAsync(FilePaneViewModel sourcePane, FilePaneViewModel targetPane, FileSystemModel file)
    {
        if (!sourcePane.CanOperate(file) || !targetPane.HasCurrentListing) return;
        var source = sourcePane.SelectedSource!;
        var target = targetPane.SelectedSource!;
        var targetDirectory = targetPane.CurrentPath;
        if (file.IsDirectory)
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            NotificationService.Instance.ShowWarning(
                localizer.GetString("FilePage.NotSupportedTitle", "不支持"),
                localizer.GetString("FilePage.NotSupportedMessage", "暂不支持直接传输文件夹"));
            return;
        }

        string sourcePath = sourcePane.GetEntryPath(file);
        string targetPath = targetPane.GetEntryPath(file);

        await CopyFileAsync(source, sourcePath, target, targetPath, file, targetPane, targetDirectory);
    }

    public async System.Threading.Tasks.Task DownloadToLocalAsync(FilePaneViewModel pane, FileSystemModel file, string path)
    {
        if (!pane.CanOperate(file) || file.IsDirectory || pane.SelectedSource!.IsLocal) return;
        await CopyFileAsync(pane.SelectedSource, pane.GetEntryPath(file), new FileSource(), path, file);
    }

    private async System.Threading.Tasks.Task CopyFileAsync(FileSource source, string sourcePath, FileSource target,
        string targetPath, FileSystemModel file, FilePaneViewModel? targetPane = null, string? targetDirectory = null)
    {

        var localizer2 = Services.Localization.LocalizationManager.Instance;
        string title = localizer2.GetString("FilePage.TransferringTitle", "传输中");
        string message = string.Format(localizer2.GetString("FilePage.TransferringMessage", "正在传输 {0}..."), file.Name);

        var taskService = TaskService.Instance;
        var toastManager = ToastManager.Instance;
        var managedTask = taskService.Start(new TaskStartOptions
        {
            Title = title,
            Description = message,
            Source = localizer2.GetString("TaskPage.DefaultSource", "通用任务"),
            IsIndeterminate = false
        });

        var toast = toastManager.ShowProgress(
            title,
            message,
            isIndeterminate: false);

        try
        {
            var progress = new System.Progress<double>(p =>
            {
                toastManager.Update(toast, currentToast =>
                {
                    currentToast.Progress = p;
                    currentToast.IsIndeterminate = false;
                });
                taskService.Update(managedTask, p);
            });

            if (source.IsLocal && target.IsLocal)
            {
                // 本地到本地
                await System.Threading.Tasks.Task.Run(() => System.IO.File.Copy(sourcePath, targetPath, true));
                toastManager.Update(toast, currentToast =>
                {
                    currentToast.Progress = 100;
                    currentToast.IsIndeterminate = false;
                });
                taskService.Update(managedTask, 100);
            }
            else if (source.IsLocal && !target.IsLocal)
            {
                // 本地到设备 (上传)
                if (target.IsRemoteAgent)
                {
                    NotificationService.Instance.ShowWarning(
                        localizer2.GetString("FilePage.NotSupportedTitle", "不支持"),
                        "当前 Agent 后端尚未提供文件上传接口。");
                    taskService.Fail(managedTask, "当前 Agent 后端尚未提供文件上传接口。");
                    toastManager.Dismiss(toast);
                    return;
                }
                else
                {
                    await target.Device!.FileManager.UploadFileAsync(sourcePath, targetPath, progress);
                }
            }
            else if (!source.IsLocal && target.IsLocal)
            {
                // 设备到本地 (下载)
                if (source.IsRemoteAgent)
                {
                    await source.AgentFileManager!.DownloadFileAsync(sourcePath, targetPath, progress);
                }
                else
                {
                    await source.Device!.FileManager.DownloadFileAsync(sourcePath, targetPath, progress);
                }
            }
            else
            {
                if (source.IsRemoteAgent || target.IsRemoteAgent)
                {
                    NotificationService.Instance.ShowWarning(
                        localizer2.GetString("FilePage.NotSupportedTitle", "不支持"),
                        "当前远程 Agent 文件管理暂不支持设备到设备直传。");
                    taskService.Fail(managedTask, "当前远程 Agent 文件管理暂不支持设备到设备直传。");
                    toastManager.Dismiss(toast);
                    return;
                }

                // 设备到设备
                if (source.Device!.Serial == target.Device!.Serial)
                {
                    // 同一设备 使用 shell cp
                    var receiver = new AdvancedSharpAdbClient.Receivers.ConsoleOutputReceiver();
                    await source.Device!.AdbClient!.ExecuteShellCommandAsync(
                        source.Device!.DeviceData,
                        $"cp -- {AdbFileManager.QuoteShellPath(sourcePath)} {AdbFileManager.QuoteShellPath(targetPath)}",
                        receiver,
                        default);
                    toastManager.Update(toast, currentToast =>
                    {
                        currentToast.Progress = 100;
                        currentToast.IsIndeterminate = false;
                    });
                    taskService.Update(managedTask, 100);
                }
                else
                {
                    // 不同设备 使用临时文件中转
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), file.Name);
                    string msg1 = localizer2.GetString("FilePage.DownloadingToTemp", "正在下载到临时目录...");
                    toastManager.Update(toast, currentToast =>
                    {
                        currentToast.Content = msg1;
                        currentToast.Progress = 0;
                        currentToast.IsIndeterminate = false;
                    });
                    taskService.Update(managedTask, 0, msg1);
                    await source.Device!.FileManager.DownloadFileAsync(sourcePath, tempFile, new System.Progress<double>(p =>
                    {
                        toastManager.Update(toast, currentToast =>
                        {
                            currentToast.Progress = p / 2;
                            currentToast.IsIndeterminate = false;
                        });
                        taskService.Update(managedTask, p / 2);
                    }));
                    
                    string msg2 = localizer2.GetString("FilePage.UploadingToTarget", "正在上传到目标设备...");
                    toastManager.Update(toast, currentToast =>
                    {
                        currentToast.Content = msg2;
                        currentToast.Progress = 50;
                        currentToast.IsIndeterminate = false;
                    });
                    taskService.Update(managedTask, 50, msg2);
                    await target.Device!.FileManager.UploadFileAsync(tempFile, targetPath, new System.Progress<double>(p =>
                    {
                        toastManager.Update(toast, currentToast =>
                        {
                            currentToast.Progress = 50 + p / 2;
                            currentToast.IsIndeterminate = false;
                        });
                        taskService.Update(managedTask, 50 + p / 2);
                    }));
                    
                    System.IO.File.Delete(tempFile);
                }
            }

            toastManager.Dismiss(toast);
            string successMsg = string.Format(localizer2.GetString("FilePage.TransferCompleteMessage", "{0} 传输成功"), file.Name);
            taskService.Complete(managedTask, successMsg);
            NotificationService.Instance.ShowSuccess(
                localizer2.GetString("FilePage.TransferCompleteTitle", "传输完成"),
                successMsg);
            if (targetPane != null && ReferenceEquals(target, targetPane.SelectedSource) && targetPane.CurrentPath == targetDirectory)
                await targetPane.LoadFilesAsync();
        }
        catch (System.Exception ex)
        {
            toastManager.Dismiss(toast);
            taskService.Fail(managedTask, ex.Message);
            NotificationService.Instance.ShowError(
                localizer2.GetString("FilePage.TransferFailedTitle", "传输失败"),
                ex.Message);
        }
    }
}
