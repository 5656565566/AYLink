using AYLink.Core.ADB;
using AYLink.Core.Models;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
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
    [ObservableProperty]
    private ObservableCollection<FileSource> _availableSources = new();

    [ObservableProperty]
    private FileSource? _selectedSource;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileSystemModel> _files = new();

    [ObservableProperty]
    private FileSystemModel? _selectedFile;

    [ObservableProperty]
    private bool _isLoading;

    public FilePaneViewModel()
    {
        RefreshSources(autoSelect: false);
    }

    public void RefreshSources(bool autoSelect = true)
    {
        var currentSelectedName = SelectedSource?.Name;
        
        AvailableSources.Clear();
        
        // 添加本地源
        AvailableSources.Add(new FileSource { Name = "本地 - 用户目录", InitialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
        var rootPath = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
        AvailableSources.Add(new FileSource { Name = "本地 - 系统盘", InitialPath = rootPath });

        // 添加设备源
        if (AdbManager.Instance.TryStartAdbServer())
        {
            var devices = AdbManager.Instance.GetConnectedDevices();
            foreach (var device in devices)
            {
                AvailableSources.Add(new FileSource { Name = $"{device.Name} - 内部存储", Device = device, InitialPath = "/" });
                AvailableSources.Add(new FileSource { Name = $"{device.Name} - SD卡", Device = device, InitialPath = "/sdcard/" });
            }
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
        SelectedSource = AvailableSources.FirstOrDefault(s => s.Name == "本地 - 用户目录");
    }

    public void SelectDevice(DeviceModel device)
    {
        // 查找已有的设备源
        var deviceSource = AvailableSources.FirstOrDefault(s => s.Device?.Serial == device.Serial && s.Name.Contains("SD卡"))
                        ?? AvailableSources.FirstOrDefault(s => s.Device?.Serial == device.Serial);

        if (deviceSource == null)
        {
            // 设备源不在列表中，手动添加
            var internalSource = new FileSource { Name = $"{device.Name} - 内部存储", Device = device, InitialPath = "/" };
            var sdCardSource = new FileSource { Name = $"{device.Name} - SD卡", Device = device, InitialPath = "/sdcard/" };
            AvailableSources.Add(internalSource);
            AvailableSources.Add(sdCardSource);
            deviceSource = sdCardSource;
        }

        SelectedSource = deviceSource;
    }

    [RelayCommand]
    public async Task DeleteFile(FileSystemModel? file)
    {
        if (file == null || file.Name == "..") return;

        var result = await DialogHelper.ShowMessageAsync("确认删除", $"确定要删除 {file.Name} 吗？", "删除", "取消");
        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary) return;

        bool success = false;
        string targetPath = SelectedSource!.IsLocal
            ? Path.Combine(CurrentPath, file.Name)
            : (CurrentPath.EndsWith("/") ? $"{CurrentPath}{file.Name}" : $"{CurrentPath}/{file.Name}");

        try
        {
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
                success = await SelectedSource.Device!.FileManager.DeleteFileAsync(targetPath);
            }

            if (success)
            {
                DialogHelper.ShowToast("删除成功", $"{file.Name} 已被删除", FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
                await LoadFilesAsync();
            }
            else
            {
                DialogHelper.ShowToast("删除失败", $"无法删除 {file.Name}", FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            DialogHelper.ShowToast("删除出错", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
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
                await LoadDeviceFilesAsync(SelectedSource.Device!, CurrentPath);
            }
        }
        catch (Exception ex)
        {
            DialogHelper.ShowToast("加载失败", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
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
                result.Add(new FileSystemModel(file.Name, (int)file.Length, false));
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
                    CurrentPath = path.Substring(0, lastSlash + 1);
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
                CurrentPath = CurrentPath.EndsWith("/") ? $"{CurrentPath}{file.Name}" : $"{CurrentPath}/{file.Name}";
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

        var result = await DialogHelper.ShowMessageAsync("确认删除", $"确定要删除 {file.Name} 吗？", "删除", "取消");
        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary) return;

        bool success = false;
        string targetPath = SelectedSource!.IsLocal
            ? Path.Combine(CurrentPath, file.Name)
            : (CurrentPath.EndsWith("/") ? $"{CurrentPath}{file.Name}" : $"{CurrentPath}/{file.Name}");

        try
        {
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
                success = await SelectedSource.Device!.FileManager.DeleteFileAsync(targetPath);
            }

            if (success)
            {
                DialogHelper.ShowToast("删除成功", $"{file.Name} 已被删除", FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
                await LoadFilesAsync();
            }
            else
            {
                DialogHelper.ShowToast("删除失败", $"无法删除 {file.Name}", FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            DialogHelper.ShowToast("删除出错", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }
}