using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AYLink.ADB;
using AYLink.Scrcpy;
using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace AYLink.UIModel;

/// <summary>
/// 存储设备 APP 信息的基本模型
/// </summary>
public class AppInfo(string name, string packageName)
{
    public string Name { get; set; } = name;
    public string PackageName { get; set; } = packageName;
}

/// <summary>
/// 存储设备信息的数据模型
/// </summary>
public class DeviceModel(DeviceData deviceData, AdbClient adbClient)
{
    /// <summary>
    /// 连接方式
    /// </summary>
    public string ConnectionType { get; set; } = DetermineConnectionType(deviceData);

    /// <summary>
    /// 设备型号
    /// </summary>
    public string Model { get; set; } = deviceData.Model;

    /// <summary>
    /// 设备名称
    /// </summary>
    public string Name { get; set; } = deviceData.Model;

    /// <summary>
    /// 设备的唯一序列号
    /// </summary>
    public string Serial { get; set; } = deviceData.Serial;
    /// <summary>
    /// 设备信息
    /// </summary>
    public DeviceData DeviceData { get; set; } = deviceData;

    /// <summary>
    /// 与此设备通信的 AdbClient 实例
    /// </summary>
    public AdbClient? AdbClient { get; set; } = adbClient;

    /// <summary>
    /// Scrcpy 的视频流 TCP 连接
    /// </summary>
    public List<Socket>? VideoStream { get; set; }

    /// <summary>
    /// Scrcpy 的音频流 TCP 连接 只能有一个
    /// </summary>
    public Socket? AudioStream { get; set; }

    /// <summary>
    /// Scrcpy 的控制流 TCP 连接
    /// </summary>
    public List<Socket>? ControlStream { get; set; }

    /// <summary>
    /// 连接状态
    /// </summary>
    public bool IsConnected { get; set; }
    /// <summary>
    /// 文件管理
    /// </summary>
    public AdbFileManager FileManager { get; set; } = new AdbFileManager(adbClient, deviceData);
    /// <summary>
    /// 连接配置
    /// </summary>
    public ServerOptions? ServerOptions { get; set; }

    /// <summary>
    /// 连接状态判断
    /// </summary>
    /// <param name="device"></param>
    /// <returns></returns>
    public static string DetermineConnectionType(DeviceData deviceData)
    {

        // 通过 TransportId 判断
        if (!string.IsNullOrEmpty(deviceData.TransportId))
        {
            if (deviceData.TransportId.Contains("usb", StringComparison.OrdinalIgnoreCase) ||
                deviceData.TransportId.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            {
                return "USB";
            }
            else if (deviceData.TransportId.Contains("local", StringComparison.OrdinalIgnoreCase) ||
                     deviceData.TransportId.Contains("network", StringComparison.OrdinalIgnoreCase))
            {
                return "WiFi";
            }
        }

        // 通过设备状态判断
        if (deviceData.State == DeviceState.Online || deviceData.State == DeviceState.Offline)
        {
            // 检查设备序列号
            if (!string.IsNullOrEmpty(deviceData.Serial))
            {
                if (deviceData.Serial.StartsWith("emulator-"))
                {
                    return "Emulator";
                }
                else if (deviceData.Serial.Contains('.') || deviceData.Serial.Contains(':'))
                {
                    return "WiFi";
                }
                else
                {
                    return "USB";
                }
            }
        }

        return "Unknown";
    }
}