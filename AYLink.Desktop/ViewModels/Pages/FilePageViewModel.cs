using AYLink.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 文件管理页 ViewModel
/// </summary>
public partial class FilePageViewModel : PageViewModelBase
{
    public override string PageKey => "File";
    public override string Title => "文件管理";

    [ObservableProperty]
    private DeviceModel? _currentDevice;

    public override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);
        
        if (parameter is DeviceModel device)
        {
            CurrentDevice = device;
            // TODO: 加载设备文件列表
        }
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        CurrentDevice = null;
    }
}
