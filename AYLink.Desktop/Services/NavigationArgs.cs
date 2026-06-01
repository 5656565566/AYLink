using System;
using AYLink.Core.Devices;

namespace AYLink.Desktop.Services;

/// <summary>
/// 导航参数基类 - 所有导航参数必须继承此类
/// 
/// 使用示例：
/// <code>
/// // 定义参数类
/// public class ScreenNavigationArgs : NavigationArgs
/// {
///     public string DeviceSerial { get; init; }
///     public string? PackageName { get; init; }
/// }
/// 
/// public class FileNavigationArgs : NavigationArgs
/// {
///     public string DeviceSerial { get; init; }
///     public string InitialPath { get; init; } = "/sdcard";
/// }
/// 
/// // 调用导航
/// NavigationService.Instance.NavigateTo("Screen", new ScreenNavigationArgs 
/// { 
///     DeviceSerial = "192.168.1.100:5555" 
/// });
/// 
/// NavigationService.Instance.NavigateTo("File", new FileNavigationArgs 
/// { 
///     DeviceSerial = "abc123",
///     InitialPath = "/sdcard/Download" 
/// });
/// </code>
/// </summary>
public abstract class NavigationArgs
{
}

/// <summary>
/// 投屏页导航参数
/// </summary>
public sealed class ScreenNavigationArgs : NavigationArgs
{
    public AYLink.Core.Models.DeviceModel? Device { get; init; }

    public DeviceDescriptor? RemoteDevice { get; init; }

    public string? ServerId { get; init; }

    public string? AppPackageName { get; init; }

    public string? AppDisplayName { get; init; }

    public bool NewDisplay { get; init; }

    public int? NewDisplayWidth { get; init; }

    public int? NewDisplayHeight { get; init; }

    public int? NewDisplayDpi { get; init; }
}

/// <summary>
/// 文件管理页导航参数
/// </summary>
public sealed class FileNavigationArgs : NavigationArgs
{
    public AYLink.Core.Models.DeviceModel? Device { get; init; }

    public DeviceDescriptor? RemoteDevice { get; init; }

    public string? ServerId { get; init; }

    public string? InitialPath { get; init; }
}

/// <summary>
/// 应用管理页导航参数
/// </summary>
public sealed class AppNavigationArgs : NavigationArgs
{
    public AYLink.Core.Models.DeviceModel? Device { get; init; }

    public DeviceDescriptor? RemoteDevice { get; init; }

    public string? ServerId { get; init; }
}

/// <summary>
/// 终端页导航参数
/// </summary>
public sealed class ShellNavigationArgs : NavigationArgs
{
    public AYLink.Core.Models.DeviceModel? Device { get; init; }

    public DeviceDescriptor? RemoteDevice { get; init; }

    public string? ServerId { get; init; }
}

/// <summary>
/// 设备设置页导航参数
/// </summary>
public sealed class DeviceSettingNavigationArgs : NavigationArgs
{
    public string DeviceSerial { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public DeviceDescriptor? RemoteDevice { get; init; }

    public string? ServerId { get; init; }
}
