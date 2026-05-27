using AYLink.Core.Agent;
using AYLink.Desktop.Services.Audio;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.Services;

/// <summary>
/// 负责把 Agent WebRTC 会话与桌面端现有 SDL 音频链路接起来
/// </summary>
internal sealed class AgentScreenSessionController : IDisposable, IAsyncDisposable
{
    private readonly AgentWebRtcSession _session;
    private readonly AudioPlayer _audioPlayer;
    private readonly Action<int, int, IntPtr, int> _videoFrameForwarder;
    private int _audioStreamId = -1;
    private bool _disposed;
    private Task? _shutdownTask;

    /// <summary>
    /// 暴露底层 Agent WebRTC 会话实例
    /// </summary>
    public AgentWebRtcSession Session => _session;

    /// <summary>
    /// 当远端视频帧被解码为 BGRA 后触发
    /// </summary>
    public event Action<int, int, IntPtr, int>? VideoFrameDecoded;

    /// <summary>
    /// 创建一个新的 Agent 屏幕会话控制器
    /// </summary>
    /// <param name="session">底层 WebRTC 会话</param>
    /// <param name="audioPlayer">桌面端统一音频播放器</param>
    public AgentScreenSessionController(AgentWebRtcSession session, AudioPlayer audioPlayer)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _videoFrameForwarder = HandleVideoFrameDecoded;

        _session.VideoFrameDecoded += _videoFrameForwarder;
        _session.AudioFrameDecoded += HandleAudioFrameDecoded;
    }

    /// <summary>
    /// 建立当前 Agent WebRTC 会话
    /// </summary>
    /// <param name="options">会话参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ConnectAsync(AgentWebRtcSessionOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureAudioStream();
        try
        {
            await _session.ConnectAsync(options, cancellationToken);
        }
        catch
        {
            CleanupAudio();
            throw;
        }
    }

    /// <summary>
    /// 主动关闭当前会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task CloseAsync(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken);

    /// <summary>
    /// 正常停止当前 Agent WebRTC 会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var shutdownTask = EnsureShutdownStarted(cancellationToken);
        await shutdownTask;
    }

    /// <summary>
    /// 释放控制器资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.VideoFrameDecoded -= _videoFrameForwarder;
        _session.AudioFrameDecoded -= HandleAudioFrameDecoded;
        CleanupAudio();
        _ = EnsureShutdownStarted(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 异步释放控制器资源
    /// </summary>
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
        _session.VideoFrameDecoded -= _videoFrameForwarder;
        _session.AudioFrameDecoded -= HandleAudioFrameDecoded;
        CleanupAudio();
        await EnsureShutdownStarted(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 确保底层会话关闭任务已经启动
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>后台关闭任务</returns>
    private Task EnsureShutdownStarted(CancellationToken cancellationToken)
    {
        _shutdownTask ??= ShutdownSessionAsync(cancellationToken);
        return _shutdownTask;
    }

    /// <summary>
    /// 后台停止并释放底层 Agent WebRTC 会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task ShutdownSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.StopAsync(cancellationToken);
        }
        finally
        {
            await _session.DisposeAsync();
        }
    }

    /// <summary>
    /// 转发会话视频帧事件
    /// </summary>
    /// <param name="width">视频宽度</param>
    /// <param name="height">视频高度</param>
    /// <param name="buffer">BGRA 像素缓冲区</param>
    /// <param name="rowBytes">每行字节数</param>
    private void HandleVideoFrameDecoded(int width, int height, IntPtr buffer, int rowBytes)
    {
        VideoFrameDecoded?.Invoke(width, height, buffer, rowBytes);
    }

    /// <summary>
    /// 确保音频播放流已经准备好
    /// </summary>
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

    /// <summary>
    /// 处理会话解码出来的 PCM 音频
    /// </summary>
    /// <param name="pcmBytes">PCM 音频字节数据</param>
    private void HandleAudioFrameDecoded(byte[] pcmBytes)
    {
        if (_audioStreamId >= 0)
        {
            _audioPlayer.StreamPush(_audioStreamId, pcmBytes);
        }
    }

    /// <summary>
    /// 清理当前音频播放流
    /// </summary>
    private void CleanupAudio()
    {
        if (_audioStreamId >= 0)
        {
            _audioPlayer.StopStream(_audioStreamId);
            _audioStreamId = -1;
        }
    }
}
