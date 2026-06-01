using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AYLink.Core.Agent;

/// <summary>
/// 用于解码 Agent WebRTC 会话收到的编码音频帧
/// </summary>
internal unsafe sealed class AgentEncodedAudioDecoder : IDisposable
{
    private int _decodedAudioFrameCount;

    /// <summary>
    /// 与桌面端 SDL 混音链路对齐的目标采样率
    /// </summary>
    public const int TargetSampleRate = 48000;

    /// <summary>
    /// 与桌面端 SDL 混音链路对齐的目标声道数
    /// </summary>
    public const int TargetChannels = 2;

    private const AVSampleFormat TargetSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;

    private AVCodecContext* _codecContext;
    private SwrContext* _resamplerContext;
    private AudioCodecsEnum _currentCodec = AudioCodecsEnum.Unknown;
    private bool _disposed;

    /// <summary>
    /// 当一帧音频被解码为 PCM 数据后触发
    /// </summary>
    public event Action<byte[]>? PcmFrameDecoded;

    /// <summary>
    /// 写入一帧编码音频数据并尝试解码
    /// </summary>
    /// <param name="format">当前帧格式</param>
    /// <param name="encodedFrame">编码帧数据</param>
    public void Decode(AudioFormat format, byte[] encodedFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (encodedFrame.Length == 0)
        {
            return;
        }

        if (_decodedAudioFrameCount < 3)
        {
            Debug.WriteLine($"[AgentWebRTC][AudioDecoder] encoded frame recv: codec={format.Codec}, bytes={encodedFrame.Length}, clockRate={format.ClockRate}, channels={format.ChannelCount}");
        }

        EnsureCodec(format);

        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        if (packet == null || frame == null)
        {
            throw new OutOfMemoryException("Failed to allocate FFmpeg packet/frame.");
        }

        try
        {
            if (ffmpeg.av_new_packet(packet, encodedFrame.Length) < 0)
            {
                throw new OutOfMemoryException("Failed to allocate audio packet buffer.");
            }

            Marshal.Copy(encodedFrame, 0, (nint)packet->data, encodedFrame.Length);
            DecodePacket(packet, frame);
            ffmpeg.av_packet_unref(packet);
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&frame);
        }
    }

    /// <summary>
    /// 释放当前解码器持有的非托管资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_resamplerContext != null)
        {
            var resampler = _resamplerContext;
            ffmpeg.swr_free(&resampler);
            _resamplerContext = null;
        }

        if (_codecContext != null)
        {
            fixed (AVCodecContext** context = &_codecContext)
            {
                ffmpeg.avcodec_free_context(context);
                _codecContext = null;
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 确保当前解码器与远端音频编码格式一致
    /// </summary>
    /// <param name="format">远端音频格式</param>
    private void EnsureCodec(AudioFormat format)
    {
        if (_codecContext != null && _currentCodec == format.Codec)
        {
            return;
        }

        ResetCodec();

        var codecId = format.Codec switch
        {
            AudioCodecsEnum.OPUS => AVCodecID.AV_CODEC_ID_OPUS,
            AudioCodecsEnum.PCM_S16LE => AVCodecID.AV_CODEC_ID_PCM_S16LE,
            _ => throw new NotSupportedException($"Unsupported audio codec: {format.Codec}.")
        };

        AVCodec* codec = ffmpeg.avcodec_find_decoder(codecId);
        if (codec == null)
        {
            throw new InvalidOperationException($"FFmpeg codec not found for {codecId}.");
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
        {
            throw new OutOfMemoryException("Failed to allocate audio codec context.");
        }

        _codecContext->sample_rate = format.ClockRate > 0 ? format.ClockRate : TargetSampleRate;
        _codecContext->pkt_timebase = new AVRational { num = 1, den = _codecContext->sample_rate };

        ffmpeg.av_channel_layout_default(&_codecContext->ch_layout, format.ChannelCount > 0 ? format.ChannelCount : TargetChannels);

        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
        {
            throw new InvalidOperationException("Failed to open audio codec.");
        }

        _currentCodec = format.Codec;
    }

    /// <summary>
    /// 重置音频解码上下文
    /// </summary>
    private void ResetCodec()
    {
        if (_resamplerContext != null)
        {
            var resampler = _resamplerContext;
            ffmpeg.swr_free(&resampler);
            _resamplerContext = null;
        }

        if (_codecContext != null)
        {
            fixed (AVCodecContext** context = &_codecContext)
            {
                ffmpeg.avcodec_free_context(context);
                _codecContext = null;
            }
        }

        _currentCodec = AudioCodecsEnum.Unknown;
    }

    /// <summary>
    /// 向 FFmpeg 解码器提交编码包并读取解码帧
    /// </summary>
    /// <param name="packet">待解码的数据包</param>
    /// <param name="frame">复用的解码帧缓冲</param>
    private void DecodePacket(AVPacket* packet, AVFrame* frame)
    {
        var result = ffmpeg.avcodec_send_packet(_codecContext, packet);
        if (result < 0 && result != ffmpeg.AVERROR(ffmpeg.EAGAIN) && result != ffmpeg.AVERROR_EOF)
        {
            throw new InvalidOperationException($"Failed to send audio packet to decoder: {result}.");
        }

        while (true)
        {
            result = ffmpeg.avcodec_receive_frame(_codecContext, frame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
            {
                break;
            }

            if (result < 0)
            {
                throw new InvalidOperationException($"Failed to receive audio frame from decoder: {result}.");
            }

            try
            {
                if (_resamplerContext == null)
                {
                    InitializeResampler(frame);
                }

                EmitPcm(frame);
            }
            finally
            {
                ffmpeg.av_frame_unref(frame);
            }
        }
    }

    /// <summary>
    /// 初始化音频重采样器
    /// </summary>
    /// <param name="frame">当前音频帧</param>
    private void InitializeResampler(AVFrame* frame)
    {
        AVChannelLayout inputLayout = new();
        AVChannelLayout outputLayout = new();
        ffmpeg.av_channel_layout_copy(&inputLayout, &frame->ch_layout);
        ffmpeg.av_channel_layout_default(&outputLayout, TargetChannels);

        fixed (SwrContext** context = &_resamplerContext)
        {
            ffmpeg.swr_alloc_set_opts2(
                context,
                &outputLayout,
                TargetSampleFormat,
                TargetSampleRate,
                &inputLayout,
                (AVSampleFormat)frame->format,
                frame->sample_rate,
                0,
                null);
        }

        if (ffmpeg.swr_init(_resamplerContext) < 0)
        {
            throw new InvalidOperationException("Failed to initialize audio resampler.");
        }

        ffmpeg.av_channel_layout_uninit(&inputLayout);
        ffmpeg.av_channel_layout_uninit(&outputLayout);
    }

    /// <summary>
    /// 将当前音频帧转换为目标 PCM 格式并向外抛出
    /// </summary>
    /// <param name="frame">当前音频帧</param>
    private void EmitPcm(AVFrame* frame)
    {
        byte* resampledData;
        if (ffmpeg.av_samples_alloc(&resampledData, null, TargetChannels, frame->nb_samples, TargetSampleFormat, 0) < 0)
        {
            throw new OutOfMemoryException("Failed to allocate audio sample buffer.");
        }

        try
        {
            var outputSamples = ffmpeg.swr_convert(
                _resamplerContext,
                &resampledData,
                frame->nb_samples,
                frame->extended_data,
                frame->nb_samples);

            if (outputSamples <= 0)
            {
                return;
            }

            var bufferSize = ffmpeg.av_samples_get_buffer_size(null, TargetChannels, outputSamples, TargetSampleFormat, 1);
            if (bufferSize <= 0)
            {
                return;
            }

            var managedBuffer = new byte[bufferSize];
            Marshal.Copy((nint)resampledData, managedBuffer, 0, bufferSize);
            _decodedAudioFrameCount++;
            if (_decodedAudioFrameCount <= 3 || _decodedAudioFrameCount % 240 == 0)
            {
                Debug.WriteLine($"[AgentWebRTC][AudioDecoder] pcm decoded: count={_decodedAudioFrameCount}, bytes={bufferSize}");
            }
            PcmFrameDecoded?.Invoke(managedBuffer);
        }
        finally
        {
            ffmpeg.av_freep(&resampledData);
        }
    }
}
