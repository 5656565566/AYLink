using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AYLink.Desktop.ViewModels.Pages;
using System.Runtime.InteropServices;

namespace AYLink.Desktop.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void SelectScrcpyServerBtn_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Scrcpy Server 文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Java Archive") { Patterns = ["*.jar", "*.*"] }
            ]
        });

        if (result.Count > 0 && DataContext is SettingsPageViewModel vm)
        {
            vm.ScrcpyServer = result[0].Path.LocalPath;
        }
    }

    private async void SelectAdbBtn_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var filter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { new FilePickerFileType("ADB Executable") { Patterns = ["adb.exe"] } }
            : null;

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 ADB 执行文件",
            AllowMultiple = false,
            FileTypeFilter = filter
        });

        if (result.Count > 0 && DataContext is SettingsPageViewModel vm)
        {
            vm.Adb = result[0].Path.LocalPath;
        }
    }

    private async void SelectFFmpegBtn_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 FFmpeg 二进制文件目录",
            AllowMultiple = false
        });

        if (result.Count > 0 && DataContext is SettingsPageViewModel vm)
        {
            vm.FFmpegBin = result[0].Path.LocalPath;
        }
    }
}
