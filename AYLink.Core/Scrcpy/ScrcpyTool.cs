using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AYLink.Core.Models;
using AYLink.Core.Utils;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AYLink.Core.Scrcpy;

public class ScrcpyTool(DeviceModel deviceModel, string scrcpyServerPath)
{
    private readonly DeviceModel _deviceModel = deviceModel;
    private readonly string _scrcpyServerPath = scrcpyServerPath;

    public async void PushScrcpyServer()
    {
        DeviceData device = _deviceModel.DeviceData;
        
        if (!File.Exists(_scrcpyServerPath))
        {
            // throw new FileNotFoundException("Scrcpy server JAR file not found");
            Debug.WriteLine($"Scrcpy server JAR file not found at {_scrcpyServerPath}");
            return;
        }
        try
        {
            using ISyncService syncService = new SyncService(AdbClient.Instance.EndPoint, device);

            var fileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead;

            await using FileStream stream = File.OpenRead(_scrcpyServerPath);
            await syncService.PushAsync(
                stream,
                "/data/local/tmp/scrcpy-server",
                fileMode,
                DateTimeOffset.Now,
                (IProgress<SyncProgressChangedEventArgs>?)null,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PushScrcpyServer failed: {ex.Message}");
            return;
        }
    }

    public ServerOptions GetServerOptions()
    {
        ServerOptions serverOptions = new()
        {
            ClientVersion = "3.3.4",
        };
        return serverOptions;
    }

    public List<string> GetEncoders()
    {
        if (_deviceModel?.DeviceData is not { } device)
        {
            return [];
        }

        var encoderList = new List<string>();
        var receiver = new ConsoleOutputReceiver();

        var adbClient = _deviceModel.AdbClient!;
        ServerOptions serverOptions = GetServerOptions();
        serverOptions.ListEncoders = true;

        try
        {
            PushScrcpyServer();

            adbClient.ExecuteRemoteCommand(serverOptions.ToString(), device, receiver);
            string output = receiver.ToString();

            if (!string.IsNullOrWhiteSpace(output))
            {
                var regex = new Regex(@"--(?:video|audio)-encoder=(\S+)");
                var matches = regex.Matches(output);

                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        string encoderName = match.Groups[1].Value.Trim();
                        encoderList.Add(encoderName);
                    }
                }
            }
        }
        catch { }

        return [.. encoderList.Distinct()];
    }

    public List<AppInfo> GetAppInfos()
    {
        DeviceData device = _deviceModel.DeviceData;
        List<AppInfo> appList = [];
        var receiver = new ConsoleOutputReceiver();

        AdbClient adbClient = _deviceModel.AdbClient!;
        ServerOptions serverOptions = GetServerOptions();
        serverOptions.ListApps = true;

        try
        {
            PushScrcpyServer();
            adbClient.ExecuteRemoteCommand(serverOptions.ToString(), device, receiver);
            string output = receiver.ToString();

            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

                // 匹配 "- GitHub   com.github.android"
                var regex = new Regex(@"^\s*[\*\-]\s*(.*?)\s+([a-zA-Z][\w\.]+)\s*$");

                foreach (var line in lines)
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        string appName = match.Groups[1].Value.Trim();
                        string package = match.Groups[2].Value.Trim();
                        appList.Add(new AppInfo ( appName, package));
                    }
                }
            }
        }
        catch
        {
            
        }

        return appList;
    }

    public Dictionary<int, (int width, int height)> GetResolutions()
    {
        DeviceData device = _deviceModel.DeviceData;

        var resolutions = new Dictionary<int, (int, int)>();
        var receiver = new ConsoleOutputReceiver();

        AdbClient adbClient = _deviceModel.AdbClient!;
        ServerOptions serverOptions = GetServerOptions();
        serverOptions.ListDisplays = true;

        try
        {
            PushScrcpyServer();
            adbClient.ExecuteRemoteCommand(serverOptions.ToString(), device, receiver);
            var output = receiver.ToString();
            Trace.WriteLine(output);

            var displayMatches = Regex.Matches(output, @"--display-id=(\d+)\s+\((\d+)x(\d+)\)");
            foreach (Match match in displayMatches)
            {
                if (match.Groups.Count == 4 &&
                    int.TryParse(match.Groups[1].Value, out int displayId) &&
                    int.TryParse(match.Groups[2].Value, out int width) &&
                    int.TryParse(match.Groups[3].Value, out int height))
                {
                    resolutions[displayId] = (width, height);
                }
            }

            if (resolutions.Count == 0)
            {
                var altMatches = Regex.Matches(output, @"Display (\d+)[\s\S]*?cur=(\d+)x(\d+)");
                foreach (Match match in altMatches)
                {
                    if (match.Groups.Count == 4 &&
                        int.TryParse(match.Groups[1].Value, out int displayId) &&
                        int.TryParse(match.Groups[2].Value, out int width) &&
                        int.TryParse(match.Groups[3].Value, out int height))
                    {
                        resolutions[displayId] = (width, height);
                    }
                }

                if (resolutions.Count == 0)
                {
                    adbClient.ExecuteRemoteCommand("wm size", device, receiver);
                    output = receiver.ToString();

                    var sizeMatch = Regex.Match(output, @"Physical size: (\d+)x(\d+)");
                    if (sizeMatch.Success &&
                        int.TryParse(sizeMatch.Groups[1].Value, out int width) &&
                        int.TryParse(sizeMatch.Groups[2].Value, out int height))
                    {
                        resolutions.Add(0, (width, height)); // Default display
                    }
                    else
                    {
                        Trace.WriteLine("无法获取设备分辨率信息 ADB 命令返回空结果");
                    }
                }
            }

            return resolutions;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("获取分辨率信息时出错", ex);
        }
    }

    public async Task<int[]> DeployServerAsync(bool isAudioAvailable)
    {
        DeviceData device = _deviceModel.DeviceData;

        PushScrcpyServer();

        int[] ports = [0, 0, 0];
        int port;

        _deviceModel.ServerOptions!.Scid = new Random().Next(10000000, 30000000);

        ServerOptions serverOptions = _deviceModel.ServerOptions;
        AdbClient adbClient = _deviceModel.AdbClient!;

        try
        {
            if (serverOptions.Video)
            {
                port = PortUtility.GetRandomUnusedPort(10000, 50000);

                await adbClient.CreateForwardAsync(
                    device,
                     $"tcp:{port}",
                     $"localabstract:scrcpy_{serverOptions.Scid}",
                    true
                );

                ports[0] = port;

                Debug.WriteLine($"视频端口转发成功: tcp:{port} -> localabstract:scrcpy_{serverOptions.Scid}");
            }

            if (_deviceModel.AudioStream != null)
            {
                serverOptions.Audio = false; // 音频流只能有一个
            }

            if (!isAudioAvailable)
            {
                serverOptions.Audio = false; // 必须可以播放音频才启用音频
            }

            if (serverOptions.Audio)
            {
                port = PortUtility.GetRandomUnusedPort(10000, 50000);

                await adbClient.CreateForwardAsync(
                    device,
                     $"tcp:{port}",
                     $"localabstract:scrcpy_{serverOptions.Scid}",
                    true
                );

                ports[1] = port;

                Debug.WriteLine($"音频端口转发成功: tcp:{port} -> localabstract:scrcpy_{serverOptions.Scid}");
            }

            if (serverOptions.Control)
            {
                port = PortUtility.GetRandomUnusedPort(10000, 50000);

                await adbClient.CreateForwardAsync(
                    device,
                     $"tcp:{port}",
                     $"localabstract:scrcpy_{serverOptions.Scid}",
                    true
                );

                ports[2] = port;

                Debug.WriteLine($"控制端口转发成功: tcp:{port} -> localabstract:scrcpy_{serverOptions.Scid}");
            }

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"端口转发失败: {ex.Message}");
        }

        Debug.WriteLine($"捕获显示器 {serverOptions.DisplayId}");

        Thread scrcpyServer = new(() =>
        {
            try
            {
                AdbClient.Instance.ExecuteRemoteCommand(serverOptions.ToString(), device, new TraceReceiver());
            }
            catch { }
        });

        scrcpyServer.Start();

        return ports;
    }
}