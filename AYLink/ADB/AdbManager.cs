using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AYLink.UIModel;
using AYLink.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.ADB;

/// <summary>
/// 用于管理 ADB 连接和设备的单例类
/// </summary>
public sealed class AdbManager
{
    private static readonly Lazy<AdbManager> lazy =
        new(() => new AdbManager());

    /// <summary>
    /// 获取 AdbManager 的唯一实例
    /// </summary>
    public static AdbManager Instance { get { return lazy.Value; } }

    private readonly Dictionary<string, DeviceModel> _connectedDevices;
    private readonly AdbClient _adbClient;
    private readonly AdbServer _adbServer = new();

    private AdbManager()
    {
        _connectedDevices = [];
        _adbClient = new AdbClient();
    }

    public bool TryStartAdbServer()
    {
        if (AdbServer.Instance.GetStatus().IsRunning)
        {
            return true;
        }

        // 根据操作系统确定 adb 可执行文件的名称
        string adbFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "adb.exe" : "adb";

        // 尝试使用本地 "ADB" 目录下的 adb
        string localAdbPath = Path.Combine("ADB", adbFileName);
        if (File.Exists(localAdbPath))
        {
            var result = _adbServer.StartServer(localAdbPath, restartServerIfNewer: false);
            if (result == StartServerResult.Started)
            {
                Debug.WriteLine($"已从本地路径启动 ADB 服务: {localAdbPath}");
                return true;
            }
        }

        // 在环境变量 PATH 中查找 adb
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable != null)
        {
            string[] paths = pathVariable.Split(Path.PathSeparator);
            foreach (string path in paths)
            {
                string adbPathInEnv = Path.Combine(path, adbFileName);
                if (File.Exists(adbPathInEnv))
                {
                    var result = _adbServer.StartServer(adbPathInEnv, restartServerIfNewer: false);
                    if (result == StartServerResult.Started)
                    {
                        Debug.WriteLine($"已从环境变量 PATH 启动 ADB 服务: {adbPathInEnv}");
                        return true;
                    }
                }
            }
        }

        try
        {
            var result = _adbServer.StartServer(adbFileName, restartServerIfNewer: false);
            if (result == StartServerResult.Started)
            {
                Debug.WriteLine($"已通过系统查找启动 ADB 服务: {adbFileName}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"尝试通过系统查找启动 ADB 服务时出错: {ex.Message}");
        }

        return false;
    }

    public void KillServer()
    {
        if (AdbServer.Instance.GetStatus().IsRunning)
        {
            _adbServer.StopServer();
        }
    }

    /// <summary>
    /// 通过 Wi-Fi 配对码配对安卓设备 (适用于 Android 11+)
    /// </summary>
    /// <param name="ipAddress">设备上显示的 IP 地址</param>
    /// <param name="pairingPort">设备上显示的配对端口</param>
    /// <param name="pairingCode">设备上显示的配对码</param>
    /// <returns>如果配对成功返回 true，否则返回 false</returns>
    public static bool PairWifiDevice(string ipAddress, int pairingPort, string pairingCode)
    {
        try
        {
            var adbClient = new AdbClient();
            Debug.WriteLine($"正在尝试配对设备: {ipAddress}:{pairingPort}...");
            adbClient.Pair(ipAddress, pairingPort, pairingCode);
            Debug.WriteLine("配对成功！现在请使用连接端口进行连接。");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"配对失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 使用指定的主机和端口连接到一个新设备。
    /// </summary>
    /// <param name="host">设备的主机名或 IP 地址。</param>
    /// <param name="port">设备的端口号。</param>
    /// <returns>成功连接则返回 DeviceModel，否则返回 null。</returns>
    public DeviceModel? ConnectDevice(string host, int port)
    {
        string deviceSerial = $"{host}:{port}";

        if (_connectedDevices.TryGetValue(deviceSerial, out var existingDevice))
        {
            if (existingDevice.IsConnected)
            {
                return existingDevice;
            }
        }

        try
        {
            var adbClient = new AdbClient();
            adbClient.Connect(host, port);
            var deviceData = adbClient.GetDevices().FirstOrDefault(d => d.Serial == deviceSerial);
            if (deviceData != null)
            {
                var deviceModel = new DeviceModel(deviceData, adbClient)
                {
                    IsConnected = true
                };
                _connectedDevices[deviceData.Serial] = deviceModel;

                return deviceModel;
            }
            else
            {
                Debug.WriteLine($"无法在连接后找到设备: {deviceSerial}。请检查设备连接和开发者选项。");
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"连接设备 {deviceSerial} 时出错: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 刷新已连接的设备列表。此方法会获取当前所有通过 ADB 连接的设备，
    /// 并将任何尚未在内部字典中的新设备添加进去。
    /// </summary>
    public async Task RefreshConnectedDevices()
    {

        if (!AdbServer.Instance.GetStatus().IsRunning)
        {
            return;
        }

        try
        {
            // 获取当前所有通过 ADB 连接的设备
            var currentDevices = await _adbClient.GetDevicesAsync();

            // 找出已经断开的设备
            var disconnectedSerials = _connectedDevices.Keys.Except(currentDevices.Select(d => d.Serial)).ToList();
            foreach (var serial in disconnectedSerials)
            {
                _connectedDevices[serial].IsConnected = false;
            }

            // 添加新发现的设备
            foreach (var deviceData in currentDevices)
            {
                if (deviceData.Name == "")
                {
                    continue;
                }

                // 如果设备不在字典中，则添加它
                if (!_connectedDevices.TryGetValue(deviceData.Serial, out DeviceModel? value))
                {
                    var deviceModel = new DeviceModel(deviceData, _adbClient)
                    {
                        IsConnected = true
                    };
                    _connectedDevices.Add(deviceData.Serial, deviceModel);
                    Debug.WriteLine($"发现并添加了新设备: {deviceData.Serial}");
                }
                else
                {
                    if (value.IsConnected == false)
                    {
                        value.DeviceData = deviceData;
                        value.IsConnected = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"刷新设备列表时出错: {ex.Message}");
        }
    }


    /// <summary>
    /// 断开与设备的连接
    /// </summary>
    /// <param name="serial">设备的序列号</param>
    public void DisconnectDevice(string serial)
    {
        if (_connectedDevices.TryGetValue(serial, out var deviceModel))
        {
            // 从字典中移除
            _connectedDevices.Remove(serial);
            Debug.WriteLine($"设备 {serial} 已断开。");
        }
    }

    /// <summary>
    /// 获取所有已连接设备的信息
    /// </summary>
    /// <returns>一个包含所有已连接设备信息的列表</returns>
    public List<DeviceModel> GetConnectedDevices()
    {
        return [.. _connectedDevices.Values];
    }

    /// <summary>
    /// 检查设备是否真实在线并可操作。
    /// 这比简单地检查设备是否在连接列表中更为可靠，因为它会尝试与设备进行一次真实的通信。
    /// </summary>
    /// <param name="serial">要检查的设备的序列号。</param>
    /// <returns>如果设备在线并能成功执行命令，则返回 true；否则返回 false。</returns>
    public bool IsDeviceTrulyOnline(string serial)
    {
        if (!_connectedDevices.TryGetValue(serial, out var deviceModel) || !deviceModel.IsConnected)
        {
            return false;
        }

        try
        {
            _adbClient.ExecuteRemoteCommand("echo", deviceModel.DeviceData);
            return true;
        }
        catch (Exception ex)
        {

            Debug.WriteLine($"设备 {serial} 检查失败，判定为离线: {ex.Message}");
            deviceModel.IsConnected = false;

            return false;
        }
    }

    /// <summary>
    /// 根据序列号获取特定设备的信息
    /// </summary>
    /// <param name="serial">设备的序列号</param>
    /// <returns>找到的设备模型，否则为 null</returns>
    public DeviceModel? GetDeviceBySerial(string serial)
    {
        _connectedDevices.TryGetValue(serial, out var deviceModel);
        return deviceModel;
    }
}