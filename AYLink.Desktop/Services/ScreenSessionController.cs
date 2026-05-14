using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Core.Scrcpy.Control;
using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Services.Input;

namespace AYLink.Desktop.Services;

internal sealed class ScreenSessionController : IDisposable
{
    private readonly DeviceModel _device;
    private readonly string? _appPackageName;
    private readonly AudioPlayer _audioPlayer;
    private ScrcpyClient? _client;
    private int _audioStreamId = -1;
    private bool _disposed;

    public bool IsFlexDisplayEnabled { get; private set; }
    public bool CanResizeDisplay { get; private set; }
    public ScrcpyClient? Client => _client;

    public event Action<int, int, IntPtr, int>? VideoFrameDecoded;

    public ScreenSessionController(DeviceModel device, string? appPackageName, AudioPlayer audioPlayer)
    {
        _device = device;
        _appPackageName = appPackageName;
        _audioPlayer = audioPlayer;
    }

    public async Task ConnectAsync(IInputProcessor inputProcessor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var deviceConfig = ConfigManager.Instance.LoadConfig<DeviceConfig>(
            HashHelper.ToMd5Hash(_device.Serial));

        _device.ServerOptions ??= new ServerOptions();
        deviceConfig.ApplyConfig(_device.ServerOptions);

        _client = new ScrcpyClient(_device);
        inputProcessor.SetSender(new ScrcpyClientCommandSender(_client));

        _client.OnVideoFrameDecoded += (width, height, bgraDataPtr, rowBytes) =>
            VideoFrameDecoded?.Invoke(width, height, bgraDataPtr, rowBytes);

        _client.OnAudioDataDecoded += pcmData =>
        {
            if (_audioStreamId >= 0)
            {
                _audioPlayer.StreamPush(_audioStreamId, pcmData);
            }
        };

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

                bool isAudioAvailable = _audioPlayer.IsAudioDeviceAvailable;
                if (isAudioAvailable && _device.ServerOptions.Audio)
                {
                    if (!_audioPlayer.IsActivate())
                    {
                        _audioPlayer.ConfigureAudioDevice();
                    }

                    _audioStreamId = _audioPlayer.StreamPlayStart(
                        AudioDecoder.TARGET_SAMPLE_RATE,
                        AudioDecoder.TARGET_CHANNELS);
                }

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
                    _client.SendControlCommand(new ControlMsgModel.ControlMsg
                    {
                        Type = ControlMsgModel.ControlMsgType.StartApp,
                        Data = _appPackageName
                    }.Serialize());
                }
            });
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public bool SendResizeDisplayIfNeeded(Size newSize, bool hasReceivedFirstVideoFrame, Size? lastResizeRequestSize)
    {
        if (!IsFlexDisplayEnabled || !CanResizeDisplay || _client == null || newSize.Width <= 0 || newSize.Height <= 0)
        {
            return false;
        }

        if (!hasReceivedFirstVideoFrame)
        {
            return false;
        }

        if (lastResizeRequestSize is Size lastSize &&
            Math.Abs(lastSize.Width - newSize.Width) < 0.5 &&
            Math.Abs(lastSize.Height - newSize.Height) < 0.5)
        {
            return false;
        }

        _client.SendControlCommand(new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.ResizeDisplay,
            Data = new ControlMsgModel.ResizeDisplayData
            {
                Width = (ushort)Math.Max(1, newSize.Width),
                Height = (ushort)Math.Max(1, newSize.Height)
            }
        }.Serialize());

        return true;
    }

    private void Cleanup()
    {
        if (_audioStreamId >= 0)
        {
            _audioPlayer.StopStream(_audioStreamId);
            _audioStreamId = -1;
        }

        _client?.DisConnect();
        _client?.Dispose();
        _client = null;
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

    private class ScrcpyClientCommandSender(ScrcpyClient client) : IControlCommandSender
    {
        private readonly ScrcpyClient _client = client;

        public void SendCommand(byte[] controlMessage)
        {
            _client.SendControlCommand(controlMessage);
        }
    }
}
