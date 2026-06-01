using AYLink.Core.Scrcpy;
using AYLink.Desktop.Services.Audio;
using System;

namespace AYLink.Desktop.Services.ScreenSessions.Local;

/// <summary>
/// 管理本地投屏会话的音频播放输出
/// </summary>
internal sealed class LocalScreenAudioOutput : IDisposable
{
    private readonly AudioPlayer _audioPlayer;
    private int _audioStreamId = -1;
    private bool _disposed;

    public LocalScreenAudioOutput(AudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
    }

    public bool Prepare(bool enableAudio)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool isAudioAvailable = _audioPlayer.IsAudioDeviceAvailable;
        if (!isAudioAvailable || !enableAudio)
        {
            return isAudioAvailable;
        }

        if (!_audioPlayer.IsActivate())
        {
            _audioPlayer.ConfigureAudioDevice();
        }

        _audioStreamId = _audioPlayer.StreamPlayStart(
            AudioDecoder.TARGET_SAMPLE_RATE,
            AudioDecoder.TARGET_CHANNELS);
        return isAudioAvailable;
    }

    public void OnAudioDecoded(byte[] pcmData)
    {
        if (_audioStreamId >= 0)
        {
            _audioPlayer.StreamPush(_audioStreamId, pcmData);
        }
    }

    public void Stop()
    {
        if (_audioStreamId >= 0)
        {
            _audioPlayer.StopStream(_audioStreamId);
            _audioStreamId = -1;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}
