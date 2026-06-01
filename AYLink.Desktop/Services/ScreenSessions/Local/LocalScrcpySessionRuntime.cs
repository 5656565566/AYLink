using Avalonia;
using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Input;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.Desktop.Services.ScreenSessions.Local;

/// <summary>
/// 管理本地 scrcpy 会话的部署、连接与底层运行时
/// </summary>
internal sealed class LocalScrcpySessionRuntime : IDisposable
{
    private readonly DeviceModel _device;
    private readonly string? _appPackageName;
    private readonly LocalScreenAudioOutput _audioOutput;
    private readonly LocalScreenControlBridge _controlBridge;
    private ScrcpyClient? _client;
    private bool _disposed;

    public ScrcpyClient? Client => _client;
    public bool IsFlexDisplayEnabled { get; private set; }
    public bool CanResizeDisplay { get; private set; }

    public event Action<int, int, IntPtr, int>? VideoFrameDecoded;

    public LocalScrcpySessionRuntime(
        DeviceModel device,
        string? appPackageName,
        LocalScreenAudioOutput audioOutput,
        LocalScreenControlBridge controlBridge)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _appPackageName = appPackageName;
        _audioOutput = audioOutput ?? throw new ArgumentNullException(nameof(audioOutput));
        _controlBridge = controlBridge ?? throw new ArgumentNullException(nameof(controlBridge));
    }

    public async Task ConnectAsync(IInputProcessor inputProcessor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(inputProcessor);

        var deviceConfig = ConfigManager.Instance.LoadConfig<DeviceConfig>(
            HashHelper.ToMd5Hash(_device.Serial));

        _device.ServerOptions ??= new ServerOptions();
        deviceConfig.ApplyConfig(_device.ServerOptions);

        _client = new ScrcpyClient(_device);
        _controlBridge.Attach(_client, inputProcessor);

        _client.OnVideoFrameDecoded += HandleVideoFrameDecoded;
        _client.OnAudioDataDecoded += _audioOutput.OnAudioDecoded;

        try
        {
            await Task.Run(async () =>
            {
                ScrcpyTool tool = ScrcpyService.Instance.Tool;
                var displays = tool.GetResolutions(_device);

                if (displays.Count == 0 || _appPackageName != null || _device.ServerOptions.DisplayId == -1)
                {
                    if (string.IsNullOrEmpty(_device.ServerOptions.NewDisplay))
                    {
                        _device.ServerOptions.NewDisplay = " ";
                    }

                    CanResizeDisplay = true;
                }
                else
                {
                    _device.ServerOptions.NewDisplay = null;
                    _device.ServerOptions.FlexDisplay = false;

                    var displayId = displays.Keys.ToArray()[0];
                    _device.ServerOptions.DisplayId = displayId;
                    inputProcessor.UpdateScreenSize(new Size(displays[displayId].height, displays[displayId].width));
                    CanResizeDisplay = false;
                }

                IsFlexDisplayEnabled = _device.ServerOptions.FlexDisplay;

                bool isAudioAvailable = _audioOutput.Prepare(_device.ServerOptions.Audio);

                var ports = await tool.DeployServerAsync(_device, isAudioAvailable);
                await Task.Delay(2000);

                if (!_client.Connect(ports))
                {
                    _device.ServerOptions = null;
                    throw new Exception("Failed to connect to scrcpy server.");
                }

                if (inputProcessor is HidInputProcessor hidProcessor)
                {
                    hidProcessor.CreateDevices();
                }

                _device.ServerOptions = null;

                if (!string.IsNullOrWhiteSpace(_appPackageName))
                {
                    _controlBridge.SendStartApp(_appPackageName);
                }
            });
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public void Cleanup()
    {
        _audioOutput.Stop();
        _controlBridge.Detach();

        if (_client != null)
        {
            _client.OnVideoFrameDecoded -= HandleVideoFrameDecoded;
            _client.OnAudioDataDecoded -= _audioOutput.OnAudioDecoded;
            _client.DisConnect();
            _client.Dispose();
            _client = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Cleanup();
        _disposed = true;
    }

    private void HandleVideoFrameDecoded(int width, int height, IntPtr bgraDataPtr, int rowBytes)
    {
        VideoFrameDecoded?.Invoke(width, height, bgraDataPtr, rowBytes);
    }
}
