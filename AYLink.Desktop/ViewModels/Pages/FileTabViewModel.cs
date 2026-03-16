using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AYLink.Core.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class FileTabViewModel : TabItemViewModelBase
{
    public FilePaneViewModel LeftPane { get; } = new();
    public FilePaneViewModel RightPane { get; } = new();

    public FileTabViewModel(DeviceModel? device = null)
    {
        Device = device;
        
        if (device != null)
        {
            Title = device.Name;
            LeftPane.SelectLocalHome();
            RightPane.SelectDevice(device);
        }
        else
        {
            Title = Services.Localization.LocalizationManager.Instance.GetString("FilePage.Title", "文件管理");
            LeftPane.SelectLocalHome();
            RightPane.SelectLocalHome();
        }
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
        if (sourcePane.SelectedSource == null || targetPane.SelectedSource == null) return;
        if (file.IsDirectory)
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            DialogHelper.ShowToast(
                localizer.GetString("FilePage.NotSupportedTitle", "不支持"),
                localizer.GetString("FilePage.NotSupportedMessage", "暂不支持直接传输文件夹"),
                FluentAvalonia.UI.Controls.InfoBarSeverity.Warning);
            return;
        }

        string sourcePath = sourcePane.SelectedSource.IsLocal
            ? System.IO.Path.Combine(sourcePane.CurrentPath, file.Name)
            : (sourcePane.CurrentPath.EndsWith("/") ? $"{sourcePane.CurrentPath}{file.Name}" : $"{sourcePane.CurrentPath}/{file.Name}");

        string targetPath = targetPane.SelectedSource.IsLocal
            ? System.IO.Path.Combine(targetPane.CurrentPath, file.Name)
            : (targetPane.CurrentPath.EndsWith("/") ? $"{targetPane.CurrentPath}{file.Name}" : $"{targetPane.CurrentPath}/{file.Name}");

        var localizer2 = Services.Localization.LocalizationManager.Instance;
        DialogHelper.ShowProgress(
            localizer2.GetString("FilePage.TransferringTitle", "传输中"),
            string.Format(localizer2.GetString("FilePage.TransferringMessage", "正在传输 {0}..."), file.Name),
            isBlocking: false, isIndeterminate: false);

        try
        {
            var progress = new System.Progress<double>(p => DialogHelper.UpdateProgress(p));

            if (sourcePane.SelectedSource.IsLocal && targetPane.SelectedSource.IsLocal)
            {
                // 本地到本地
                await System.Threading.Tasks.Task.Run(() => System.IO.File.Copy(sourcePath, targetPath, true));
                DialogHelper.UpdateProgress(100);
            }
            else if (sourcePane.SelectedSource.IsLocal && !targetPane.SelectedSource.IsLocal)
            {
                // 本地到设备 (上传)
                await targetPane.SelectedSource.Device!.FileManager.UploadFileAsync(sourcePath, targetPath, progress);
            }
            else if (!sourcePane.SelectedSource.IsLocal && targetPane.SelectedSource.IsLocal)
            {
                // 设备到本地 (下载)
                await sourcePane.SelectedSource.Device!.FileManager.DownloadFileAsync(sourcePath, targetPath, progress);
            }
            else
            {
                // 设备到设备
                if (sourcePane.SelectedSource.Device!.Serial == targetPane.SelectedSource.Device!.Serial)
                {
                    // 同一设备 使用 shell cp
                    var receiver = new AdvancedSharpAdbClient.Receivers.ConsoleOutputReceiver();
                    await sourcePane.SelectedSource.Device!.AdbClient!.ExecuteShellCommandAsync(
                        sourcePane.SelectedSource.Device!.DeviceData,
                        $"cp \"{sourcePath}\" \"{targetPath}\"",
                        receiver,
                        default);
                    DialogHelper.UpdateProgress(100);
                }
                else
                {
                    // 不同设备，使用临时文件中转
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), file.Name);
                    DialogHelper.UpdateProgress(0, localizer2.GetString("FilePage.DownloadingToTemp", "正在下载到临时目录..."));
                    await sourcePane.SelectedSource.Device!.FileManager.DownloadFileAsync(sourcePath, tempFile, new System.Progress<double>(p => DialogHelper.UpdateProgress(p / 2)));
                    
                    DialogHelper.UpdateProgress(50, localizer2.GetString("FilePage.UploadingToTarget", "正在上传到目标设备..."));
                    await targetPane.SelectedSource.Device!.FileManager.UploadFileAsync(tempFile, targetPath, new System.Progress<double>(p => DialogHelper.UpdateProgress(50 + p / 2)));
                    
                    System.IO.File.Delete(tempFile);
                }
            }

            DialogHelper.CloseProgress();
            DialogHelper.ShowToast(
                localizer2.GetString("FilePage.TransferCompleteTitle", "传输完成"),
                string.Format(localizer2.GetString("FilePage.TransferCompleteMessage", "{0} 传输成功"), file.Name),
                FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
            await targetPane.LoadFilesAsync();
        }
        catch (System.Exception ex)
        {
            DialogHelper.CloseProgress();
            DialogHelper.ShowToast(
                localizer2.GetString("FilePage.TransferFailedTitle", "传输失败"),
                ex.Message,
                FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }
}
