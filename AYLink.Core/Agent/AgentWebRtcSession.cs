using Newtonsoft.Json;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Core.Agent;

/// <summary>
/// 为 Agent WebRTC 会话提供访问令牌的委托
/// </summary>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>可用于当前请求的访问令牌</returns>
public delegate Task<string> AgentAccessTokenProvider(CancellationToken cancellationToken);

/// <summary>
/// Agent WebRTC 会话连接状态
/// </summary>
public enum AgentWebRtcSessionState
{
    /// <summary>
    /// 当前尚未建立连接
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 正在创建票据和 PeerConnection
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// 已收到远端应答并进入媒体协商阶段
    /// </summary>
    Negotiating = 2,

    /// <summary>
    /// 媒体连接已经建立
    /// </summary>
    Connected = 3,

    /// <summary>
    /// 会话已经关闭
    /// </summary>
    Closed = 4,

    /// <summary>
    /// 建连或运行过程中发生错误
    /// </summary>
    Faulted = 5
}

/// <summary>
/// Agent WebRTC 会话参数
/// </summary>
public sealed class AgentWebRtcSessionOptions
{
    /// <summary>
    /// Agent 端设备标识
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 可选的目标应用包名
    /// </summary>
    public string AppPackage { get; set; } = string.Empty;

    /// <summary>
    /// 可选的目标应用显示名称
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// 是否请求新建显示
    /// </summary>
    public bool NewDisplay { get; set; }

    /// <summary>
    /// 新建显示宽度
    /// </summary>
    public int? NewDisplayWidth { get; set; }

    /// <summary>
    /// 新建显示高度
    /// </summary>
    public int? NewDisplayHeight { get; set; }

    /// <summary>
    /// 新建显示 DPI
    /// </summary>
    public int? NewDisplayDpi { get; set; }

    /// <summary>
    /// 本地覆盖后的 WebRTC 网络设置
    /// 若为空则直接从 Agent 端拉取默认设置
    /// </summary>
    public AgentWebRtcNetworkSettingsDto? PreferredNetworkSettings { get; set; }
}

/// <summary>
/// 面向桌面端的 Agent WebRTC 会话
/// 负责建立 PeerConnection、收发信令、解码媒体并转发控制指令
/// </summary>
public sealed class AgentWebRtcSession : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 当前 Agent WebRTC 音频输出采样率
    /// </summary>
    public const int AudioSampleRate = AgentEncodedAudioDecoder.TargetSampleRate;

    /// <summary>
    /// 当前 Agent WebRTC 音频输出声道数
    /// </summary>
    public const int AudioChannelCount = AgentEncodedAudioDecoder.TargetChannels;

    private readonly AgentApiClient _client;
    private readonly AgentAccessTokenProvider _accessTokenProvider;
    private readonly AgentEncodedVideoDecoder _videoDecoder = new();
    private readonly AgentEncodedAudioDecoder _audioDecoder = new();
    private readonly SemaphoreSlim _signalWriteLock = new(1, 1);
    private readonly SemaphoreSlim _stopLock = new(1, 1);

    private RTCPeerConnection? _peerConnection;
    private ClientWebSocket? _signalSocket;
    private RTCDataChannel? _controlChannel;
    private RTCDataChannel? _metaControlChannel;
    private RTCDataChannel? _pointerMoveChannel;
    private CancellationTokenSource? _sessionCts;
    private Task? _signalLoopTask;
    private Task? _heartbeatTask;
    private bool _disposed;
    private bool _isStopping;
    private bool _isStopped;
    private AudioFormat? _audioFormat;
    private int _videoRtpPacketCount;
    private int _videoFrameCount;
    private int _audioRtpPacketCount;

    /// <summary>
    /// 当前会话状态
    /// </summary>
    public AgentWebRtcSessionState State { get; private set; } = AgentWebRtcSessionState.Idle;

    /// <summary>
    /// 当前会话对应的 Agent 设备标识
    /// </summary>
    public string DeviceId { get; private set; } = string.Empty;

    /// <summary>
    /// 当前会话标识
    /// </summary>
    public string SessionId { get; private set; } = string.Empty;

    /// <summary>
    /// 当前状态变更时触发
    /// </summary>
    public event Action<AgentWebRtcSessionState>? StateChanged;

    /// <summary>
    /// 当一帧视频被解码为 BGRA 后触发
    /// </summary>
    public event Action<int, int, IntPtr, int>? VideoFrameDecoded;

    /// <summary>
    /// 当一帧音频被解码为 PCM 后触发
    /// </summary>
    public event Action<byte[]>? AudioFrameDecoded;

    /// <summary>
    /// 当会话内部发生异常时触发
    /// </summary>
    public event Action<Exception>? SessionError;

    /// <summary>
    /// 指示当前会话是否已经进入停止或已停止状态
    /// </summary>
    private bool IsStoppingOrStopped => _isStopping || _isStopped;

    /// <summary>
    /// 创建一个新的 Agent WebRTC 会话实例
    /// </summary>
    /// <param name="client">Agent API 客户端</param>
    /// <param name="accessTokenProvider">访问令牌提供器</param>
    public AgentWebRtcSession(AgentApiClient client, AgentAccessTokenProvider accessTokenProvider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));

        _videoDecoder.FrameDecoded += (width, height, buffer, rowBytes) =>
            VideoFrameDecoded?.Invoke(width, height, buffer, rowBytes);
        _audioDecoder.PcmFrameDecoded += pcm => AudioFrameDecoded?.Invoke(pcm);
    }

    /// <summary>
    /// 建立一个新的 Agent WebRTC 会话
    /// </summary>
    /// <param name="options">会话参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ConnectAsync(AgentWebRtcSessionOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_peerConnection != null)
        {
            throw new InvalidOperationException("The WebRTC session is already connected.");
        }

        if (string.IsNullOrWhiteSpace(options.DeviceId))
        {
            throw new ArgumentException("Device ID cannot be empty.", nameof(options));
        }

        DeviceId = options.DeviceId.Trim();
        _isStopping = false;
        _isStopped = false;
        _audioFormat = null;
        UpdateState(AgentWebRtcSessionState.Connecting);
        Debug.WriteLine($"[AgentWebRTC] connect start: deviceId={DeviceId}, appPackage={options.AppPackage}, newDisplay={options.NewDisplay}");

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionToken = _sessionCts.Token;
        try
        {
            var accessToken = await _accessTokenProvider(sessionToken);
            var ticket = await _client.CreateWebRtcTicketAsync(accessToken, new AgentWebRtcTicketRequest
            {
                DeviceId = DeviceId,
                AppPackage = options.AppPackage.Trim(),
                AppName = options.AppName.Trim(),
                NewDisplay = options.NewDisplay,
                NewDisplayWidth = options.NewDisplayWidth,
                NewDisplayHeight = options.NewDisplayHeight,
                NewDisplayDpi = options.NewDisplayDpi
            }, sessionToken);

            SessionId = ticket.SessionId;
            Debug.WriteLine($"[AgentWebRTC] ticket created: sessionId={SessionId}, expiresIn={ticket.ExpiresInSeconds}s");

            var networkSettings = options.PreferredNetworkSettings ??
                                  await _client.GetControlWebRtcNetworkSettingsAsync(accessToken, sessionToken);
            Debug.WriteLine($"[AgentWebRTC] network settings loaded: iceServers={networkSettings.IceServers.Count}, policy={networkSettings.IceTransportPolicy}");
            _peerConnection = CreatePeerConnection(networkSettings);
            BindPeerConnection(_peerConnection);
            await CreateDataChannelsAsync(_peerConnection);
            Debug.WriteLine("[AgentWebRTC] data channels created");

            _signalSocket = new ClientWebSocket();
            var signalUri = _client.BuildWebRtcSignalUri(ticket.Ticket);
            Debug.WriteLine($"[AgentWebRTC] connecting signal websocket: {signalUri}");
            await _signalSocket.ConnectAsync(signalUri, sessionToken);
            Debug.WriteLine("[AgentWebRTC] signal websocket connected");

            var offer = _peerConnection.createOffer(null);
            await _peerConnection.setLocalDescription(offer);
            var localDescription = _peerConnection.localDescription;
            var localSdp = localDescription?.sdp?.ToString();
            await SendSignalMessageAsync(new AgentWebRtcSignalEnvelope
            {
                Type = localDescription?.type.ToString(),
                Sdp = localSdp
            }, sessionToken);
            Debug.WriteLine($"[AgentWebRTC] local offer sent: type={localDescription?.type}, sdpLength={localSdp?.Length ?? 0}");

            UpdateState(AgentWebRtcSessionState.Negotiating);
            _signalLoopTask = RunSignalLoopAsync(sessionToken);
            _heartbeatTask = RunHeartbeatLoopAsync(sessionToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentWebRTC] connect failed: {ex}");
            UpdateState(AgentWebRtcSessionState.Faulted);
            SessionError?.Invoke(ex);
            await CloseCoreAsync(true);
            throw;
        }
    }

    /// <summary>
    /// 向控制 DataChannel 发送控制消息
    /// </summary>
    /// <param name="payload">控制负载</param>
    public void SendControl(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return;
        }

        _controlChannel?.send(payload);
    }

    /// <summary>
    /// 向指针移动 DataChannel 发送高频控制消息
    /// </summary>
    /// <param name="payload">控制负载</param>
    public void SendPointerMove(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return;
        }

        (_pointerMoveChannel ?? _controlChannel)?.send(payload);
    }

    /// <summary>
    /// 主动关闭当前会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task CloseAsync(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken);

    /// <summary>
    /// 正常停止当前会话，并尽最大努力通知 Agent 端释放会话资源
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _stopLock.WaitAsync(cancellationToken);
        try
        {
            if (_isStopped)
            {
                return;
            }

            var deviceId = DeviceId;
            var sessionId = SessionId;
            await CloseCoreAsync(false);

            if (!string.IsNullOrWhiteSpace(deviceId) && !string.IsNullOrWhiteSpace(sessionId))
            {
                try
                {
                    var accessToken = await _accessTokenProvider(cancellationToken);
                    await _client.ReleaseScrcpySessionAsync(accessToken, deviceId, sessionId, cancellationToken);
                }
                catch (Exception ex)
                {
                    SessionError?.Invoke(ex);
                }
            }
        }
        finally
        {
            _stopLock.Release();
        }
    }

    /// <summary>
    /// 释放当前会话的托管和非托管资源
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步释放当前会话
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CloseCoreAsync(false);
        _signalWriteLock.Dispose();
        _stopLock.Dispose();
        _videoDecoder.Dispose();
        _audioDecoder.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 创建并配置 PeerConnection
    /// </summary>
    /// <param name="settings">当前会话使用的网络设置</param>
    /// <returns>已初始化的 PeerConnection</returns>
    private RTCPeerConnection CreatePeerConnection(AgentWebRtcNetworkSettingsDto settings)
    {
        var config = new RTCConfiguration
        {
            iceServers = BuildIceServers(settings),
            iceTransportPolicy = string.Equals(settings.IceTransportPolicy, "relay", StringComparison.OrdinalIgnoreCase)
                ? RTCIceTransportPolicy.relay
                : RTCIceTransportPolicy.all
        };

        var peerConnection = new RTCPeerConnection(config);
        peerConnection.addTrack(new MediaStreamTrack(
            [new VideoFormat(VideoCodecsEnum.H264, 96, 90000, "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f")],
            MediaStreamStatusEnum.RecvOnly));
        peerConnection.addTrack(new MediaStreamTrack(
            [new AudioFormat(AudioCodecsEnum.OPUS, 111, 48000, 2, "minptime=10;useinbandfec=1")],
            MediaStreamStatusEnum.RecvOnly));

        return peerConnection;
    }

    /// <summary>
    /// 绑定 PeerConnection 的核心事件
    /// </summary>
    /// <param name="peerConnection">待绑定的 PeerConnection</param>
    private void BindPeerConnection(RTCPeerConnection peerConnection)
    {
        peerConnection.OnStarted += () =>
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            Debug.WriteLine("[AgentWebRTC] peer session started");
        };

        peerConnection.OnVideoFormatsNegotiated += formats =>
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            VideoFormat? first = formats.Count > 0 ? formats[0] : null;
            Debug.WriteLine($"[AgentWebRTC] video formats negotiated: count={formats.Count}, first={first?.Codec}");
        };

        peerConnection.OnVideoFrameReceived += (_, _, frame, format) =>
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            try
            {
                _videoFrameCount++;
                if (_videoFrameCount <= 3 || _videoFrameCount % 120 == 0)
                {
                    Debug.WriteLine($"[AgentWebRTC] video frame event: count={_videoFrameCount}, codec={format.Codec}, bytes={frame.Length}");
                }
                _videoDecoder.Decode(format, frame);
            }
            catch (Exception ex)
            {
                SessionError?.Invoke(ex);
            }
        };

        peerConnection.OnAudioFormatsNegotiated += formats =>
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            _audioFormat = formats.Count > 0 ? formats[0] : null;
            Debug.WriteLine($"[AgentWebRTC] audio formats negotiated: count={formats.Count}, first={_audioFormat?.Codec}");
        };

        peerConnection.OnRtpPacketReceived += (_, mediaType, packet) =>
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            if (mediaType == SDPMediaTypesEnum.video)
            {
                _videoRtpPacketCount++;
                if (_videoRtpPacketCount <= 5 || _videoRtpPacketCount % 240 == 0)
                {
                    Debug.WriteLine($"[AgentWebRTC] video rtp packet: count={_videoRtpPacketCount}, payload={packet.Payload.Length}, marker={packet.Header.MarkerBit}, pt={packet.Header.PayloadType}");
                }
            }

            if (mediaType != SDPMediaTypesEnum.audio || _audioFormat is not { } audioFormat)
            {
                return;
            }

            try
            {
                _audioRtpPacketCount++;
                if (_audioRtpPacketCount <= 5 || _audioRtpPacketCount % 240 == 0)
                {
                    Debug.WriteLine($"[AgentWebRTC] audio rtp packet: count={_audioRtpPacketCount}, payload={packet.Payload.Length}, marker={packet.Header.MarkerBit}, pt={packet.Header.PayloadType}");
                }
                _audioDecoder.Decode(audioFormat, packet.Payload);
            }
            catch (Exception ex)
            {
                SessionError?.Invoke(ex);
            }
        };

        peerConnection.onicecandidate += candidate =>
        {
            if (IsStoppingOrStopped || candidate == null || _signalSocket?.State != WebSocketState.Open)
            {
                return;
            }

            Debug.WriteLine($"[AgentWebRTC] local candidate: mid={candidate.sdpMid}, index={candidate.sdpMLineIndex}, candidate={candidate.candidate}");

            _ = SendSignalMessageAsync(new AgentWebRtcIceCandidateEnvelope
            {
                Candidate = candidate.candidate,
                SdpMid = candidate.sdpMid,
                SdpMLineIndex = candidate.sdpMLineIndex
            }, _sessionCts?.Token ?? CancellationToken.None);
        };

        peerConnection.onconnectionstatechange += state =>
        {
            if (!IsStoppingOrStopped)
            {
                Debug.WriteLine($"[AgentWebRTC] connection state: {state}");
            }

            if (state == RTCPeerConnectionState.connected)
            {
                UpdateState(AgentWebRtcSessionState.Connected);
                return;
            }

            if (state == RTCPeerConnectionState.closed)
            {
                UpdateState(AgentWebRtcSessionState.Closed);
                return;
            }

            if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
            {
                UpdateState(AgentWebRtcSessionState.Faulted);
            }
        };

        peerConnection.ondatachannel += channel => BindDataChannel(channel);
    }

    /// <summary>
    /// 创建桌面端主动建立的控制 DataChannel
    /// </summary>
    /// <param name="peerConnection">当前 PeerConnection</param>
    private async Task CreateDataChannelsAsync(RTCPeerConnection peerConnection)
    {
        _controlChannel = await peerConnection.createDataChannel("control", new RTCDataChannelInit());
        BindDataChannel(_controlChannel);
        Debug.WriteLine("[AgentWebRTC] control data channel created");

        _metaControlChannel = await peerConnection.createDataChannel("control-meta", new RTCDataChannelInit());
        BindDataChannel(_metaControlChannel);
        Debug.WriteLine("[AgentWebRTC] meta control data channel created");

        _pointerMoveChannel = await peerConnection.createDataChannel("pointer-move", new RTCDataChannelInit
        {
            ordered = false,
            maxRetransmits = 0
        });
        BindDataChannel(_pointerMoveChannel);
        Debug.WriteLine("[AgentWebRTC] pointer move data channel created");
    }

    /// <summary>
    /// 绑定 DataChannel 生命周期事件
    /// </summary>
    /// <param name="channel">当前数据通道</param>
    private void BindDataChannel(RTCDataChannel channel)
    {
        channel.onerror += error =>
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.WriteLine($"[AgentWebRTC] data channel error ({channel.label}): {error}");
                SessionError?.Invoke(new InvalidOperationException(error));
            }
        };

        channel.onopen += () =>
        {
            if (!IsStoppingOrStopped)
            {
                Debug.WriteLine($"[AgentWebRTC] data channel open: {channel.label}");
            }
        };
        channel.onclose += () =>
        {
            if (!IsStoppingOrStopped)
            {
                Debug.WriteLine($"[AgentWebRTC] data channel closed: {channel.label}");
            }
        };
    }

    /// <summary>
    /// 持续处理信令 WebSocket 消息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task RunSignalLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (_signalSocket != null &&
                   _signalSocket.State == WebSocketState.Open &&
                   !cancellationToken.IsCancellationRequested)
            {
                var result = await ReceiveSignalMessageAsync(buffer, cancellationToken);
                if (result == null)
                {
                    if (!IsStoppingOrStopped)
                    {
                        Debug.WriteLine("[AgentWebRTC] signal loop closed by remote");
                    }
                    break;
                }

                if (!IsStoppingOrStopped)
                {
                    Debug.WriteLine($"[AgentWebRTC] signal message recv: {result}");
                }
                await HandleSignalMessageAsync(result, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!IsStoppingOrStopped)
            {
                Debug.WriteLine($"[AgentWebRTC] signal loop failed: {ex}");
                UpdateState(AgentWebRtcSessionState.Faulted);
                SessionError?.Invoke(ex);
            }
        }
    }

    /// <summary>
    /// 周期性向 Agent 发送会话保活
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   !string.IsNullOrWhiteSpace(DeviceId) &&
                   !string.IsNullOrWhiteSpace(SessionId))
            {
                await Task.Delay(HeartbeatInterval, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var accessToken = await _accessTokenProvider(cancellationToken);
                var heartbeat = await _client.TouchScrcpySessionAsync(accessToken, DeviceId, SessionId, cancellationToken);
                if (!IsStoppingOrStopped)
                {
                    Debug.WriteLine($"[AgentWebRTC] heartbeat: success={heartbeat.Success}, deviceId={DeviceId}, sessionId={SessionId}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!IsStoppingOrStopped)
            {
                Debug.WriteLine($"[AgentWebRTC] heartbeat failed: {ex}");
                SessionError?.Invoke(ex);
            }
        }
    }

    /// <summary>
    /// 发送一条信令消息
    /// </summary>
    /// <param name="payload">信令负载</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task SendSignalMessageAsync(object payload, CancellationToken cancellationToken)
    {
        if (_signalSocket == null || _signalSocket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonConvert.SerializeObject(payload);
        if (!IsStoppingOrStopped)
        {
            Debug.WriteLine($"[AgentWebRTC] signal message send: {json}");
        }
        var bytes = Encoding.UTF8.GetBytes(json);

        await _signalWriteLock.WaitAsync(cancellationToken);
        try
        {
            if (_signalSocket.State == WebSocketState.Open)
            {
                await _signalSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            _signalWriteLock.Release();
        }
    }

    /// <summary>
    /// 接收一条完整的信令文本消息
    /// </summary>
    /// <param name="buffer">接收缓冲区</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>信令文本；如果连接结束则返回 null</returns>
    private async Task<string?> ReceiveSignalMessageAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        if (_signalSocket == null)
        {
            return null;
        }

        using var output = new System.IO.MemoryStream();
        while (true)
        {
            var result = await _signalSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
            }
        }
    }

    /// <summary>
    /// 处理一条信令消息
    /// </summary>
    /// <param name="message">信令文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task HandleSignalMessageAsync(string message, CancellationToken cancellationToken)
    {
        var envelope = JsonConvert.DeserializeObject<AgentWebRtcSignalEnvelope>(message);
        if (envelope == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Sdp) && string.Equals(envelope.Type, "answer", StringComparison.OrdinalIgnoreCase))
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            Debug.WriteLine("[AgentWebRTC] applying remote answer");
            if (_peerConnection != null)
            {
                var result = _peerConnection.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.answer,
                    sdp = envelope.Sdp
                });

                if (result != SetDescriptionResultEnum.OK)
                {
                    throw new InvalidOperationException($"Failed to apply remote answer: {result}.");
                }

                Debug.WriteLine("[AgentWebRTC] starting peer session");
                await _peerConnection.Start();
                Debug.WriteLine("[AgentWebRTC] peer session start completed");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Candidate))
        {
            if (IsStoppingOrStopped)
            {
                return;
            }

            Debug.WriteLine($"[AgentWebRTC] applying remote candidate: mid={envelope.SdpMid}, index={envelope.SdpMLineIndex}, candidate={envelope.Candidate}");
            _peerConnection?.addIceCandidate(new RTCIceCandidateInit
            {
                candidate = envelope.Candidate,
                sdpMid = envelope.SdpMid,
                sdpMLineIndex = envelope.SdpMLineIndex ?? 0
            });
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 关闭底层连接资源
    /// </summary>
    /// <param name="faulted">是否因为错误关闭</param>
    private async Task CloseCoreAsync(bool faulted)
    {
        if (_isStopped)
        {
            return;
        }

        _isStopping = true;
        VideoFrameDecoded = null;
        AudioFrameDecoded = null;
        _sessionCts?.Cancel();
        Debug.WriteLine($"[AgentWebRTC] closing core: faulted={faulted}, deviceId={DeviceId}, sessionId={SessionId}");

        if (_signalSocket != null)
        {
            try
            {
                if (_signalSocket.State == WebSocketState.Open)
                {
                    await _signalSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                }
            }
            catch
            {
            }
            finally
            {
                _signalSocket.Dispose();
                _signalSocket = null;
            }
        }

        _controlChannel?.close();
        _metaControlChannel?.close();
        _pointerMoveChannel?.close();
        _controlChannel = null;
        _metaControlChannel = null;
        _pointerMoveChannel = null;

        _peerConnection?.Close("closing");
        _peerConnection = null;

        if (_signalLoopTask != null)
        {
            try
            {
                await _signalLoopTask;
            }
            catch
            {
            }
            _signalLoopTask = null;
        }

        if (_heartbeatTask != null)
        {
            try
            {
                await _heartbeatTask;
            }
            catch
            {
            }
            _heartbeatTask = null;
        }

        _sessionCts?.Dispose();
        _sessionCts = null;

        _audioFormat = null;
        SessionId = string.Empty;
        _isStopped = true;
        _isStopping = false;
        if (faulted)
        {
            return;
        }

        UpdateState(AgentWebRtcSessionState.Closed);
    }

    /// <summary>
    /// 将当前会话状态更新并通知外部
    /// </summary>
    /// <param name="state">新的会话状态</param>
    private void UpdateState(AgentWebRtcSessionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        Debug.WriteLine($"[AgentWebRTC] state -> {state}");
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// 将服务端网络设置转换为 SIPSorcery 可识别的 ICE 服务器配置
    /// </summary>
    /// <param name="settings">服务端网络设置</param>
    /// <returns>ICE 服务器列表</returns>
    private static List<RTCIceServer> BuildIceServers(AgentWebRtcNetworkSettingsDto settings)
    {
        var servers = new List<RTCIceServer>();
        foreach (var server in settings.IceServers)
        {
            if (server.Urls.Count == 0)
            {
                continue;
            }

            foreach (var url in server.Urls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                servers.Add(new RTCIceServer
                {
                    urls = url.Trim(),
                    username = server.Username,
                    credential = server.Credential
                });
            }
        }

        return servers;
    }
}

/// <summary>
/// Agent WebRTC 信令消息模型
/// </summary>
internal sealed class AgentWebRtcSignalEnvelope
{
    /// <summary>
    /// SDP 消息类型
    /// </summary>
    [JsonProperty("type")]
    public string? Type { get; set; }

    /// <summary>
    /// SDP 文本
    /// </summary>
    [JsonProperty("sdp")]
    public string? Sdp { get; set; }

    /// <summary>
    /// ICE Candidate 文本
    /// </summary>
    [JsonProperty("candidate")]
    public string? Candidate { get; set; }

    /// <summary>
    /// Candidate 对应的中间描述标识
    /// </summary>
    [JsonProperty("sdpMid")]
    public string? SdpMid { get; set; }

    /// <summary>
    /// Candidate 对应的媒体行索引
    /// </summary>
    [JsonProperty("sdpMLineIndex")]
    public ushort? SdpMLineIndex { get; set; }
}

/// <summary>
/// Agent WebRTC Candidate 上行信令模型
/// </summary>
internal sealed class AgentWebRtcIceCandidateEnvelope
{
    /// <summary>
    /// ICE Candidate 文本
    /// </summary>
    [JsonProperty("candidate")]
    public string Candidate { get; set; } = string.Empty;

    /// <summary>
    /// Candidate 对应的中间描述标识
    /// </summary>
    [JsonProperty("sdpMid")]
    public string? SdpMid { get; set; }

    /// <summary>
    /// Candidate 对应的媒体行索引
    /// </summary>
    [JsonProperty("sdpMLineIndex")]
    public ushort SdpMLineIndex { get; set; }
}
