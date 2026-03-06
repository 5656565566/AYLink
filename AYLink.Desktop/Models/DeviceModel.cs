using CommunityToolkit.Mvvm.ComponentModel;

namespace AYLink.Desktop.Models;

/// <summary>
/// 设备信息模型 - UI 展示用
/// </summary>
public partial class DeviceModel : ObservableObject
{
    /// <summary>
    /// 设备名称/型号
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 设备序列号
    /// </summary>
    [ObservableProperty]
    private string _serial = string.Empty;

    /// <summary>
    /// 连接方式：WiFi / USB
    /// </summary>
    [ObservableProperty]
    private string _connectionType = string.Empty;

    /// <summary>
    /// 设备是否在线
    /// </summary>
    [ObservableProperty]
    private bool _isConnected;
}
