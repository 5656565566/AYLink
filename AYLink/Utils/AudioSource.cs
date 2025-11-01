using SDL;
using System;
using System.Diagnostics;
using static SDL.SDL3;

namespace AYLink.Utils;

/// <summary>
/// 代表一个独立的、可转换和缓冲的音频源。
/// 此类由 AudioPlayer 在内部管理，包含了自动清理和延迟控制的逻辑。
/// </summary>
internal unsafe class AudioSource : IDisposable
{
    /// <summary>
    /// 当缓冲区中的数据量超过这个毫秒数时，就触发数据丢弃，以防止延迟累积。
    /// </summary>
    private const int HIGH_WATER_MARK_MS = 300;

    /// <summary>
    /// 丢弃数据后，缓冲区中剩余的数据量。
    /// </summary>
    private const int TARGET_LATENCY_MS = 100;

    public int Id { get; }
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// 获取此音源最后一次活动（接收或产生数据）的高精度时间戳。
    /// 用于外部的自动清理逻辑。
    /// </summary>
    public long LastActivityTimestamp { get; private set; }

    private readonly SDL_AudioStream* _stream;
    private readonly int _bytesPerSecond;
    private bool _disposed;

    /// <summary>
    /// 创建一个新的音频源实例。
    /// </summary>
    /// <param name="id">唯一的标识ID。</param>
    /// <param name="inputSpec">此音源的输入音频格式。</param>
    /// <param name="outputSpec">混音器期望的输出音频格式。</param>
    public AudioSource(int id, SDL_AudioSpec inputSpec, SDL_AudioSpec outputSpec)
    {
        Id = id;
        UpdateActivity(); // 初始化时标记为活跃

        // 根据输出规格计算每秒的字节数，用于在毫秒和字节之间进行转换
        _bytesPerSecond = outputSpec.freq * outputSpec.channels * 2; // S16LE格式，每个采样点占2字节

        var inSpec = inputSpec;
        var outSpec = outputSpec;
        _stream = SDL_CreateAudioStream(&inSpec, &outSpec);
        if (_stream == null)
        {
            throw new Exception($"为音源 {id} 创建 SDL_CreateAudioStream 失败: {SDL_GetError()}");
        }
    }

    /// <summary>
    /// 更新此音源的最后活动时间戳。
    /// </summary>
    public void UpdateActivity()
    {
        LastActivityTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// 向此音源的转换流中推送原始 PCM 数据。
    /// </summary>
    public void PushData(byte[] pcmBytes)
    {
        if (_disposed) return;

        UpdateActivity(); // 收到新数据，标记为活跃

        fixed (byte* p = pcmBytes)
        {
            if (!SDL_PutAudioStreamData(_stream, (nint)p, pcmBytes.Length))
            {
                Debug.WriteLine($"为音源 {Id} 调用 SDL_PutAudioStreamData 失败: {SDL_GetError()}");
            }
        }
    }

    /// <summary>
    /// 从转换流中获取已准备好用于混音的数据。
    /// </summary>
    public int GetConvertedData(Span<byte> buffer)
    {
        if (_disposed) return 0;

        int availableBytes = SDL_GetAudioStreamAvailable(_stream);
        int highWaterMarkBytes = (_bytesPerSecond * HIGH_WATER_MARK_MS) / 1000;

        // 检查缓冲区中的数据量
        if (availableBytes > highWaterMarkBytes)
        {
            int targetBytes = (_bytesPerSecond * TARGET_LATENCY_MS) / 1000;
            int bytesToDiscard = availableBytes - targetBytes;

            Debug.WriteLine($"音源 {Id} 延迟过高 ({(float)availableBytes / _bytesPerSecond * 1000:F0}ms)。正在丢弃 {(float)bytesToDiscard / _bytesPerSecond * 1000:F0}ms ");

            // 丢弃掉多余的部分
            const int discardBufferSize = 4096;
            var discardBuffer = stackalloc byte[discardBufferSize];

            while (bytesToDiscard > 0)
            {
                int bytesToRead = Math.Min(bytesToDiscard, discardBufferSize);
                int bytesActuallyRead = SDL_GetAudioStreamData(_stream, (nint)discardBuffer, bytesToRead);

                if (bytesActuallyRead <= 0) break; // 流中已没有更多数据可供丢弃

                bytesToDiscard -= bytesActuallyRead;
            }
        }

        // 在延迟恢复正常后，读取实际需要的数据
        int bytesRead = 0;
        fixed (byte* p = buffer)
        {
            bytesRead = SDL_GetAudioStreamData(_stream, (nint)p, buffer.Length);
        }

        if (bytesRead > 0)
        {
            UpdateActivity(); // 成功读取到数据，标记为活跃
        }

        return bytesRead;
    }

    /// <summary>
    /// 释放此音源占用的非托管资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        SDL_DestroyAudioStream(_stream);
        _disposed = true;
    }
}