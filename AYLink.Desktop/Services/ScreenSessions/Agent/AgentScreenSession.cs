using AYLink.Core.Agent;
using AYLink.Core.Devices;
using AYLink.Core.Scrcpy.Control;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Services.Input;
using Avalonia;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.Services.ScreenSessions.Agent;

/// <summary>
/// 面向页面层的 Agent 远程投屏会话门面
/// </summary>
internal sealed class AgentScreenSession : IScreenSession, IResizableScreenSession
{
    private readonly AgentServerRuntime _remoteServer;
    private readonly AgentWebRtcSession _session;
    private readonly AgentWebRtcSessionOptions _options;
    private readonly AudioPlayer _audioPlayer;
    private readonly Action<int, int, IntPtr, int> _videoFrameForwarder;
    private readonly Action<byte[]> _audioFrameForwarder;
    private readonly bool _newDisplayRequested;
    private int _audioStreamId = -1;
    private bool _disposed;
    private bool _isFlexDisplayEnabled;
    private Task? _shutdownTask;

    public ScreenSessionState State { get; private set; } = ScreenSessionState.Idle;
    public bool IsFlexDisplayEnabled => _newDisplayRequested && _isFlexDisplayEnabled;

    public event Action<ScreenSessionState>? StateChanged;
    public event Action<int, int, IntPtr, int>? VideoFrameDecoded;
    public event Action<Exception>? SessionError;

    public AgentScreenSession(
        DeviceDescriptor remoteDevice,
        AgentServerRuntime remoteServer,
        string? appPackageName,
        string? appDisplayName,
        AudioPlayer audioPlayer,
        bool newDisplay = false,
        int? newDisplayWidth = null,
        int? newDisplayHeight = null,
        int? newDisplayDpi = null)
    {
        ArgumentNullException.ThrowIfNull(remoteDevice);
        ArgumentNullException.ThrowIfNull(remoteServer);

        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _remoteServer = remoteServer;
        _session = new AgentWebRtcSession(remoteServer.Client, remoteServer.EnsureAccessTokenAsync);
        _options = CreateSessionOptions(remoteDevice, remoteServer.Config, appPackageName, appDisplayName, newDisplay, newDisplayWidth, newDisplayHeight, newDisplayDpi);
        _newDisplayRequested = newDisplay;
        _videoFrameForwarder = HandleVideoFrameDecoded;
        _audioFrameForwarder = HandleAudioFrameDecoded;

        _session.VideoFrameDecoded += _videoFrameForwarder;
        _session.AudioFrameDecoded += _audioFrameForwarder;
        _session.StateChanged += HandleSessionStateChanged;
        _session.SessionError += HandleSessionError;
    }

    public async Task StartAsync(IInputProcessor inputProcessor, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(inputProcessor);

        await LoadSessionContextAsync(cancellationToken);
        EnsureAudioStream();
        inputProcessor.SetSender(new AgentSessionCommandSender(_session));

        try
        {
            UpdateState(ScreenSessionState.Connecting);
            await _session.ConnectAsync(_options, cancellationToken);
        }
        catch
        {
            CleanupAudio();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var shutdownTask = EnsureShutdownStarted(cancellationToken);
        await shutdownTask;
    }

    public void SendControl(byte[] payload)
    {
        _session.SendControl(payload);
    }

    public void SendPointerMove(byte[] payload)
    {
        _session.SendPointerMove(payload);
    }

    public bool SendResizeDisplayIfNeeded(Size newSize, bool hasReceivedFirstVideoFrame, Size? lastResizeRequestSize)
    {
        if (!IsFlexDisplayEnabled || newSize.Width <= 0 || newSize.Height <= 0)
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

        _session.SendControl(new ControlMsgModel.ControlMsg
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachSessionHandlers();
        CleanupAudio();
        _ = EnsureShutdownStarted(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            if (_shutdownTask != null)
            {
                await _shutdownTask;
            }

            return;
        }

        _disposed = true;
        DetachSessionHandlers();
        CleanupAudio();
        await EnsureShutdownStarted(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    private static AgentWebRtcSessionOptions CreateSessionOptions(
        DeviceDescriptor remoteDevice,
        AgentServerConfig config,
        string? appPackageName,
        string? appDisplayName,
        bool newDisplay,
        int? newDisplayWidth,
        int? newDisplayHeight,
        int? newDisplayDpi)
    {
        var options = new AgentWebRtcSessionOptions
        {
            DeviceId = remoteDevice.RemoteDeviceId?.ToString() ?? string.Empty,
            AppPackage = appPackageName ?? string.Empty,
            AppName = appDisplayName ?? string.Empty,
            NewDisplay = newDisplay,
            NewDisplayWidth = newDisplayWidth,
            NewDisplayHeight = newDisplayHeight,
            NewDisplayDpi = newDisplayDpi
        };

        if (config.EnableWebRtcOverride)
        {
            var settings = new AgentWebRtcNetworkSettingsDto
            {
                IceTransportPolicy = string.IsNullOrWhiteSpace(config.LocalIceTransportPolicy) ? "all" : config.LocalIceTransportPolicy.Trim()
            };

            foreach (var server in config.LocalIceServers)
            {
                if (string.IsNullOrWhiteSpace(server.Address))
                {
                    continue;
                }

                settings.IceServers.Add(new AgentWebRtcIceServerDto
                {
                    Urls = [server.Address.Trim()]
                });
            }

            options.PreferredNetworkSettings = settings;
        }

        return options;
    }

    private void HandleVideoFrameDecoded(int width, int height, IntPtr buffer, int rowBytes)
    {
        VideoFrameDecoded?.Invoke(width, height, buffer, rowBytes);
    }

    private void HandleAudioFrameDecoded(byte[] pcmBytes)
    {
        if (_audioStreamId >= 0)
        {
            _audioPlayer.StreamPush(_audioStreamId, pcmBytes);
        }
    }

    private void HandleSessionStateChanged(AgentWebRtcSessionState state)
    {
        var mapped = state switch
        {
            AgentWebRtcSessionState.Connecting => ScreenSessionState.Connecting,
            AgentWebRtcSessionState.Negotiating => ScreenSessionState.Connecting,
            AgentWebRtcSessionState.Connected => ScreenSessionState.Connected,
            AgentWebRtcSessionState.Closed => ScreenSessionState.Closed,
            AgentWebRtcSessionState.Faulted => ScreenSessionState.Faulted,
            _ => ScreenSessionState.Idle
        };

        UpdateState(mapped);
    }

    private void HandleSessionError(Exception ex)
    {
        SessionError?.Invoke(ex);
    }

    private void EnsureAudioStream()
    {
        if (_audioStreamId >= 0)
        {
            return;
        }

        if (!_audioPlayer.IsActivate())
        {
            _audioPlayer.ConfigureAudioDevice();
        }

        _audioStreamId = _audioPlayer.StreamPlayStart(
            AgentWebRtcSession.AudioSampleRate,
            AgentWebRtcSession.AudioChannelCount);
    }

    private void CleanupAudio()
    {
        if (_audioStreamId >= 0)
        {
            _audioPlayer.StopStream(_audioStreamId);
            _audioStreamId = -1;
        }
    }

    private async Task LoadSessionContextAsync(CancellationToken cancellationToken)
    {
        _isFlexDisplayEnabled = false;
        if (!_newDisplayRequested || !int.TryParse(_options.DeviceId, out var deviceId) || deviceId <= 0)
        {
            return;
        }

        try
        {
            var accessToken = await _remoteServer.EnsureAccessTokenAsync(cancellationToken);
            var settings = await _remoteServer.Client.GetDeviceSettingsAsync(accessToken, deviceId, cancellationToken);
            _remoteServer.TouchSuccess();
            _isFlexDisplayEnabled = settings.FlexDisplay;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentScreenSession] LoadSessionContextAsync failed: {ex}");
        }
    }

    private void DetachSessionHandlers()
    {
        _session.VideoFrameDecoded -= _videoFrameForwarder;
        _session.AudioFrameDecoded -= _audioFrameForwarder;
        _session.StateChanged -= HandleSessionStateChanged;
        _session.SessionError -= HandleSessionError;
    }

    private Task EnsureShutdownStarted(CancellationToken cancellationToken)
    {
        _shutdownTask ??= ShutdownSessionAsync(cancellationToken);
        return _shutdownTask;
    }

    private async Task ShutdownSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentScreenSession] StopAsync failed: {ex}");
        }
        finally
        {
            await _session.DisposeAsync();
        }
    }

    private void UpdateState(ScreenSessionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }

    private sealed class AgentSessionCommandSender(AgentWebRtcSession session) : IControlCommandSender
    {
        private readonly AgentWebRtcSession _session = session;

        public void SendCommand(byte[] controlMessage)
        {
            _session.SendControl(controlMessage);
        }
    }
}
