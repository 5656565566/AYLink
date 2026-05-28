using AYLink.Core.ADB;
using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Agent;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class FilePaneViewModel : ViewModelBase
{
    private readonly ObservableCollection<FileSource> _customSources = [];

    [ObservableProperty]
    public partial ObservableCollection<FileSource> AvailableSources { get; set; } = [];

    [ObservableProperty]
    public partial FileSource? SelectedSource { get; set; }

    [ObservableProperty]
    public partial string CurrentPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<FileSystemModel> Files { get; set; } = [];

    [ObservableProperty]
    public partial FileSystemModel? SelectedFile { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public FilePaneViewModel()
    {
        RefreshSources(autoSelect: false);
    }

    public void RefreshSources(bool autoSelect = true)
    {
        var currentSelectedName = SelectedSource?.Name;
        
        AvailableSources.Clear();
        
        var localizer = Services.Localization.LocalizationManager.Instance;
        // 添加本地源
        AvailableSources.Add(new FileSource { Name = localizer.GetString("FilePage.SourceLocalHome", "本地 - 用户目录"), InitialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
        var rootPath = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
        AvailableSources.Add(new FileSource { Name = localizer.GetString("FilePage.SourceLocalRoot", "本地 - 系统盘"), InitialPath = rootPath });

        // 添加设备源
        if (AdbManager.Instance.TryStartAdbServer())
        {
            var devices = AdbManager.Instance.GetConnectedDevices();
            foreach (var device in devices)
            {
                AvailableSources.Add(new FileSource { Name = string.Format(localizer.GetString("FilePage.SourceAndroidInternal", "{0} - 内部存储"), device.Name), Device = device, InitialPath = "/" });
                AvailableSources.Add(new FileSource { Name = string.Format(localizer.GetString("FilePage.SourceAndroidSdCard", "{0} - SD卡"), device.Name), Device = device, InitialPath = "/sdcard/" });
            }
        }

        foreach (var source in _customSources)
        {
            AvailableSources.Add(source);
        }

        if (autoSelect)
        {
            // 恢复选中状态或默认选中第一个
            if (currentSelectedName != null)
            {
                SelectedSource = AvailableSources.FirstOrDefault(s => s.Name == currentSelectedName) ?? AvailableSources.FirstOrDefault();
            }
            else
            {
                SelectedSource = AvailableSources.FirstOrDefault();
            }
        }
    }

    public void SelectLocalHome()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        SelectedSource = AvailableSources.FirstOrDefault(s => s.Name == localizer.GetString("FilePage.SourceLocalHome", "本地 - 用户目录"));
    }

    public void SelectDevice(DeviceModel device)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        // 查找已有的设备源
        var sdCardSuffix = localizer.GetString("FilePage.SourceAndroidSdCardSuffix", "SD卡");
        var deviceSource = AvailableSources.FirstOrDefault(s => s.Device?.Serial == device.Serial && s.Name.Contains(sdCardSuffix))
                        ?? AvailableSources.FirstOrDefault(s => s.Device?.Serial == device.Serial);

        if (deviceSource == null)
        {
            // 设备源不在列表中，手动添加
            var internalSource = new FileSource { Name = string.Format(localizer.GetString("FilePage.SourceAndroidInternal", "{0} - 内部存储"), device.Name), Device = device, InitialPath = "/" };
            var sdCardSource = new FileSource { Name = string.Format(localizer.GetString("FilePage.SourceAndroidSdCard", "{0} - SD卡"), device.Name), Device = device, InitialPath = "/sdcard/" };
            AvailableSources.Add(internalSource);
            AvailableSources.Add(sdCardSource);
            deviceSource = sdCardSource;
        }

        SelectedSource = deviceSource;
    }

    public void SelectRemoteDevice(DeviceDescriptor remoteDevice, string serverId, string? initialPath = null)
    {
        var runtime = Services.AgentSessionService.Instance.FindServer(serverId)
            ?? throw new InvalidOperationException($"未找到远程服务器 {serverId}");

        var localizer = Services.Localization.LocalizationManager.Instance;
        var basePath = string.IsNullOrWhiteSpace(initialPath) ? "/sdcard/" : initialPath.Trim();
        var internalSource = new FileSource
        {
            Name = string.Format(localizer.GetString("FilePage.SourceAndroidInternal", "{0} - 内部存储"), remoteDevice.Name),
            AgentFileManager = new AgentFileManager(runtime, remoteDevice.RemoteDeviceId ?? 0),
            InitialPath = "/sdcard/"
        };

        var customSource = new FileSource
        {
            Name = initialSourceName(remoteDevice.Name),
            AgentFileManager = internalSource.AgentFileManager,
            InitialPath = basePath
        };

        _customSources.Clear();
        _customSources.Add(internalSource);
        if (!string.Equals(basePath, "/sdcard/", StringComparison.OrdinalIgnoreCase))
        {
            _customSources.Add(customSource);
        }

        RefreshSources(autoSelect: false);
        SelectedSource = AvailableSources.FirstOrDefault(source =>
            source.AgentFileManager != null &&
            string.Equals(source.InitialPath, basePath, StringComparison.OrdinalIgnoreCase))
            ?? AvailableSources.FirstOrDefault(source => source.AgentFileManager != null);

        string initialSourceName(string deviceName)
            => string.Format(localizer.GetString("FilePage.SourceAndroidInternal", "{0} - 内部存储"), deviceName);
    }

    [RelayCommand]
    public async Task DeleteFile(FileSystemModel? file)
    {
        if (file == null || file.Name == "..") return;

        var localizer = Services.Localization.LocalizationManager.Instance;
        var result = await DialogService.ShowMessageAsync(
            localizer.GetString("FilePage.ConfirmDeleteTitle", "确认删除"),
            string.Format(localizer.GetString("FilePage.ConfirmDeleteMessage", "确定要删除 {0} 吗？"), file.Name),
            localizer.GetString("FilePage.DeleteButton", "删除"),
            localizer.GetString("Dialog.Cancel", "取消"));
        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary) return;
        string targetPath = SelectedSource!.IsLocal
            ? Path.Combine(CurrentPath, file.Name)
            : (CurrentPath.EndsWith('/') ? $"{CurrentPath}{file.Name}" : $"{CurrentPath}/{file.Name}");

        try
        {

            bool success;
            if (SelectedSource.IsLocal)
            {
                if (file.IsDirectory)
                {
                    Directory.Delete(targetPath, true);
                }
                else
                {
                    File.Delete(targetPath);
                }
                success = true;
            }
            else
            {
                success = SelectedSource.IsRemoteAgent
                    ? await SelectedSource.AgentFileManager!.DeleteFileAsync(targetPath)
                    : await SelectedSource.Device!.FileManager.DeleteFileAsync(targetPath);
            }

            if (success)
            {
                NotificationService.Instance.ShowSuccess(
                    localizer.GetString("FilePage.DeleteSuccessTitle", "删除成功"),
                    string.Format(localizer.GetString("FilePage.DeleteSuccessMessage", "{0} 已被删除"), file.Name));
                await LoadFilesAsync();
            }
            else
            {
                NotificationService.Instance.ShowError(
                    localizer.GetString("FilePage.DeleteFailedTitle", "删除失败"),
                    string.Format(localizer.GetString("FilePage.DeleteFailedMessage", "无法删除 {0}"), file.Name));
            }
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError(
                localizer.GetString("FilePage.DeleteErrorTitle", "删除出错"),
                ex.Message);
        }
    }

    partial void OnSelectedSourceChanged(FileSource? value)
    {
        if (value != null)
        {
            CurrentPath = value.InitialPath;
            _ = LoadFilesAsync();
        }
        else
        {
            Files.Clear();
            CurrentPath = string.Empty;
        }
    }

    [RelayCommand]
    public async Task LoadFilesAsync()
    {
        if (SelectedSource == null || string.IsNullOrWhiteSpace(CurrentPath)) return;

        IsLoading = true;
        Files.Clear();

        try
        {
            if (SelectedSource.IsLocal)
            {
                await LoadLocalFilesAsync(CurrentPath);
            }
            else
            {
                if (SelectedSource.IsRemoteAgent)
                {
                    await LoadRemoteFilesAsync(SelectedSource.AgentFileManager!, CurrentPath);
                }
                else
                {
                    await LoadDeviceFilesAsync(SelectedSource.Device!, CurrentPath);
                }
            }
        }
        catch (Exception ex)
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            NotificationService.Instance.ShowError(
                localizer.GetString("FilePage.LoadFailedTitle", "加载失败"),
                ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadLocalFilesAsync(string path)
    {
        var items = await Task.Run(() =>
        {
            var dirInfo = new DirectoryInfo(path);
            if (!dirInfo.Exists) throw new DirectoryNotFoundException($"目录不存在: {path}");

            var result = new ObservableCollection<FileSystemModel>();
            
            // 添加返回上一级
            var parent = dirInfo.Parent;
            if (parent != null)
            {
                result.Add(new FileSystemModel("..", 0, true));
            }

            foreach (var dir in dirInfo.GetDirectories())
            {
                result.Add(new FileSystemModel(dir.Name, 0, true));
            }
            foreach (var file in dirInfo.GetFiles())
            {
                result.Add(new FileSystemModel(file.Name, (uint)file.Length, false));
            }

            return result;
        });

        Files = items;
    }

    private async Task LoadDeviceFilesAsync(DeviceModel device, string path)
    {
        var items = await device.FileManager.ListDirectoryAsync(path);
        Files = items;
    }

    private async Task LoadRemoteFilesAsync(AgentFileManager manager, string path)
    {
        var items = await manager.ListDirectoryAsync(path);
        Files = items;
    }

    [RelayCommand]
    public async Task NavigateTo(FileSystemModel? file)
    {
        if (file == null || !file.IsDirectory) return;

        if (file.Name == "..")
        {
            // 返回上一级
            if (SelectedSource!.IsLocal)
            {
                var parent = Directory.GetParent(CurrentPath);
                if (parent != null)
                {
                    CurrentPath = parent.FullName;
                }
            }
            else
            {
                var path = CurrentPath.TrimEnd('/');
                var lastSlash = path.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    CurrentPath = path[..(lastSlash + 1)];
                }
                else
                {
                    CurrentPath = "/";
                }
            }
        }
        else
        {
            // 进入目录
            if (SelectedSource!.IsLocal)
            {
                CurrentPath = Path.Combine(CurrentPath, file.Name);
            }
            else
            {
                CurrentPath = CurrentPath.EndsWith('/') ? $"{CurrentPath}{file.Name}" : $"{CurrentPath}/{file.Name}";
            }
        }

        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        RefreshSources();
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task Delete(FileSystemModel? file)
    {
        if (file == null || file.Name == "..") return;

        var localizer = Services.Localization.LocalizationManager.Instance;
        var result = await DialogService.ShowMessageAsync(
            localizer.GetString("FilePage.ConfirmDeleteTitle", "确认删除"),
            string.Format(localizer.GetString("FilePage.ConfirmDeleteMessage", "确定要删除 {0} 吗？"), file.Name),
            localizer.GetString("FilePage.DeleteButton", "删除"),
            localizer.GetString("Dialog.Cancel", "取消"));
        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary) return;
        string targetPath = SelectedSource!.IsLocal
            ? Path.Combine(CurrentPath, file.Name)
            : (CurrentPath.EndsWith('/') ? $"{CurrentPath}{file.Name}" : $"{CurrentPath}/{file.Name}");

        try
        {

            bool success;
            if (SelectedSource.IsLocal)
            {
                if (file.IsDirectory)
                {
                    Directory.Delete(targetPath, true);
                }
                else
                {
                    File.Delete(targetPath);
                }
                success = true;
            }
            else
            {
                success = SelectedSource.IsRemoteAgent
                    ? await SelectedSource.AgentFileManager!.DeleteFileAsync(targetPath)
                    : await SelectedSource.Device!.FileManager.DeleteFileAsync(targetPath);
            }

            if (success)
            {
                NotificationService.Instance.ShowSuccess(
                    localizer.GetString("FilePage.DeleteSuccessTitle", "删除成功"),
                    string.Format(localizer.GetString("FilePage.DeleteSuccessMessage", "{0} 已被删除"), file.Name));
                await LoadFilesAsync();
            }
            else
            {
                NotificationService.Instance.ShowError(
                    localizer.GetString("FilePage.DeleteFailedTitle", "删除失败"),
                    string.Format(localizer.GetString("FilePage.DeleteFailedMessage", "无法删除 {0}"), file.Name));
            }
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError(
                localizer.GetString("FilePage.DeleteErrorTitle", "删除出错"),
                ex.Message);
        }
    }
}
