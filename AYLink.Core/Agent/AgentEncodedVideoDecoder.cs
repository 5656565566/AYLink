using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AYLink.Core.Agent;

/// <summary>
/// 用于解码 Agent WebRTC 会话收到的编码视频帧
/// </summary>
internal unsafe sealed class AgentEncodedVideoDecoder : IDisposable
{
    private int _decodedFrameCount;

    private AVCodecContext* _codecContext;
    private SwsContext* _swsContext;
    private AVFrame* _frame;

    private int _lastWidth;
    private int _lastHeight;
    private AVPixelFormat _lastPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;

    private IntPtr _bgraBuffer = IntPtr.Zero;
    private int _bgraBufferSize;
    private int _bgraRowBytes;
    private bool _disposed;

    /// <summary>
    /// 当一帧视频被解码并转换为 BGRA 后触发
    /// </summary>
    public event Action<int, int, IntPtr, int>? FrameDecoded;

    /// <summary>
    /// 写入一帧编码视频数据并尝试解码
    /// </summary>
    /// <param name="format">当前帧格式</param>
    /// <param name="encodedFrame">编码帧数据</param>
    public void Decode(VideoFormat format, byte[] encodedFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (encodedFrame.Length == 0)
        {
            return;
        }

        if (_decodedFrameCount < 3)
        {
            Debug.WriteLine($"[AgentWebRTC][VideoDecoder] encoded frame recv: codec={format.Codec}, bytes={encodedFrame.Length}");
        }

        EnsureCodec(format);

        var normalizedFrame = NormalizeFrame(format, encodedFrame);
        AVPacket* packet = ffmpeg.av_packet_alloc();
        if (packet == null)
        {
            throw new OutOfMemoryException("Failed to allocate AVPacket.");
        }

        try
        {
            if (ffmpeg.av_new_packet(packet, normalizedFrame.Length) < 0)
            {
                throw new OutOfMemoryException("Failed to allocate packet buffer.");
            }

            Marshal.Copy(normalizedFrame, 0, (nint)packet->data, normalizedFrame.Length);
            DecodePacket(packet);
            ffmpeg.av_packet_unref(packet);
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
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

        if (_frame != null)
        {
            fixed (AVFrame** frame = &_frame)
            {
                ffmpeg.av_frame_free(frame);
                _frame = null;
            }
        }

        if (_codecContext != null)
        {
            fixed (AVCodecContext** context = &_codecContext)
            {
                ffmpeg.avcodec_free_context(context);
                _codecContext = null;
            }
        }

        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        if (_bgraBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_bgraBuffer);
            _bgraBuffer = IntPtr.Zero;
            _bgraBufferSize = 0;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 确保当前解码器与远端视频编码格式一致
    /// </summary>
    /// <param name="format">远端视频格式</param>
    private void EnsureCodec(VideoFormat format)
    {
        var codecId = format.Codec switch
        {
            VideoCodecsEnum.H264 => AVCodecID.AV_CODEC_ID_H264,
            VideoCodecsEnum.H265 => AVCodecID.AV_CODEC_ID_HEVC,
            VideoCodecsEnum.VP8 => AVCodecID.AV_CODEC_ID_VP8,
            VideoCodecsEnum.VP9 => AVCodecID.AV_CODEC_ID_VP9,
            _ => throw new NotSupportedException($"Unsupported video codec: {format.Codec}.")
        };

        if (_codecContext != null && _codecContext->codec_id == codecId)
        {
            return;
        }

        ResetCodec();

        AVCodec* codec = ffmpeg.avcodec_find_decoder(codecId);
        if (codec == null)
        {
            throw new InvalidOperationException($"FFmpeg codec not found for {codecId}.");
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
        {
            throw new OutOfMemoryException("Failed to allocate codec context.");
        }

        _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        _codecContext->thread_count = 0;
        _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME;

        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
        {
            throw new InvalidOperationException("Failed to open video codec.");
        }

        _frame = ffmpeg.av_frame_alloc();
        if (_frame == null)
        {
            throw new OutOfMemoryException("Failed to allocate AVFrame.");
        }
    }

    /// <summary>
    /// 重置视频解码上下文
    /// </summary>
    private void ResetCodec()
    {
        if (_frame != null)
        {
            fixed (AVFrame** frame = &_frame)
            {
                ffmpeg.av_frame_free(frame);
                _frame = null;
            }
        }

        if (_codecContext != null)
        {
            fixed (AVCodecContext** context = &_codecContext)
            {
                ffmpeg.avcodec_free_context(context);
                _codecContext = null;
            }
        }

        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        _lastWidth = 0;
        _lastHeight = 0;
        _lastPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    }

    /// <summary>
    /// 向 FFmpeg 解码器提交编码包并读取解码帧
    /// </summary>
    /// <param name="packet">待解码的数据包</param>
    private void DecodePacket(AVPacket* packet)
    {
        var result = ffmpeg.avcodec_send_packet(_codecContext, packet);
        if (result < 0 && result != ffmpeg.AVERROR(ffmpeg.EAGAIN) && result != ffmpeg.AVERROR_EOF)
        {
            throw new InvalidOperationException($"Failed to send video packet to decoder: {result}.");
        }

        while (true)
        {
            result = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
            {
                break;
            }

            if (result < 0)
            {
                throw new InvalidOperationException($"Failed to receive video frame from decoder: {result}.");
            }

            try
            {
                ProcessFrame(_frame);
            }
            finally
            {
                ffmpeg.av_frame_unref(_frame);
            }
        }
    }

    /// <summary>
    /// 将解码后的帧转换为 BGRA 并抛给上层
    /// </summary>
    /// <param name="frame">FFmpeg 解码帧</param>
    private void ProcessFrame(AVFrame* frame)
    {
        var width = frame->width;
        var height = frame->height;
        var pixelFormat = (AVPixelFormat)frame->format;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_swsContext == null || width != _lastWidth || height != _lastHeight || pixelFormat != _lastPixelFormat)
        {
            if (_swsContext != null)
            {
                ffmpeg.sws_freeContext(_swsContext);
            }

            _swsContext = ffmpeg.sws_getContext(
                width,
                height,
                pixelFormat,
                width,
                height,
                AVPixelFormat.AV_PIX_FMT_BGRA,
                (int)SwsFlags.SWS_BILINEAR,
                null,
                null,
                null);

            if (_swsContext == null)
            {
                throw new InvalidOperationException("Failed to create SwsContext for video frame conversion.");
            }

            _lastWidth = width;
            _lastHeight = height;
            _lastPixelFormat = pixelFormat;

            var requiredSize = width * height * 4;
            if (_bgraBufferSize < requiredSize)
            {
                if (_bgraBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_bgraBuffer);
                }

                _bgraBuffer = Marshal.AllocHGlobal(requiredSize);
                _bgraBufferSize = requiredSize;
            }

            _bgraRowBytes = width * 4;
        }

        byte_ptrArray4 destinationData = default;
        int_array4 destinationLines = default;
        destinationData[0] = (byte*)_bgraBuffer;
        destinationLines[0] = _bgraRowBytes;

        ffmpeg.sws_scale(
            _swsContext,
            frame->data,
            frame->linesize,
            0,
            height,
            destinationData,
            destinationLines);

        _decodedFrameCount++;
        if (_decodedFrameCount <= 3 || _decodedFrameCount % 120 == 0)
        {
            Debug.WriteLine($"[AgentWebRTC][VideoDecoder] frame decoded: count={_decodedFrameCount}, size={width}x{height}, rowBytes={_bgraRowBytes}");
        }

        FrameDecoded?.Invoke(width, height, _bgraBuffer, _bgraRowBytes);
    }

    /// <summary>
    /// 对远端视频帧做解码前归一化
    /// </summary>
    /// <param name="format">远端视频格式</param>
    /// <param name="encodedFrame">原始编码帧</param>
    /// <returns>可直接送入 FFmpeg 的编码帧</returns>
    private static byte[] NormalizeFrame(VideoFormat format, byte[] encodedFrame)
    {
        return format.Codec switch
        {
            VideoCodecsEnum.H264 or VideoCodecsEnum.H265 => NormalizeAnnexB(encodedFrame),
            _ => encodedFrame
        };
    }

    /// <summary>
    /// 将可能的长度前缀 NAL 数据转换为 Annex-B 格式
    /// </summary>
    /// <param name="encodedFrame">原始编码帧</param>
    /// <returns>Annex-B 编码帧</returns>
    private static byte[] NormalizeAnnexB(byte[] encodedFrame)
    {
        if (StartsWithAnnexBStartCode(encodedFrame))
        {
            return encodedFrame;
        }

        for (var lengthSize = 4; lengthSize >= 1; lengthSize--)
        {
            if (TryConvertLengthPrefixedNalUnits(encodedFrame, lengthSize, out var converted))
            {
                return converted;
            }
        }

        return encodedFrame;
    }

    /// <summary>
    /// 判断当前字节流是否已经是 Annex-B 起始码格式
    /// </summary>
    /// <param name="buffer">待判断的数据</param>
    /// <returns>是否为 Annex-B</returns>
    private static bool StartsWithAnnexBStartCode(byte[] buffer)
    {
        return buffer.Length >= 4 &&
               ((buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 1) ||
                (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0 && buffer[3] == 1));
    }

    /// <summary>
    /// 尝试把长度前缀 NAL 序列转换为 Annex-B 格式
    /// </summary>
    /// <param name="buffer">原始帧</param>
    /// <param name="lengthSize">长度前缀字节数</param>
    /// <param name="converted">转换后的结果</param>
    /// <returns>是否转换成功</returns>
    private static bool TryConvertLengthPrefixedNalUnits(byte[] buffer, int lengthSize, out byte[] converted)
    {
        converted = [];

        if (lengthSize <= 0 || buffer.Length <= lengthSize)
        {
            return false;
        }

        var offset = 0;
        using var output = new System.IO.MemoryStream(buffer.Length + 32);
        while (offset < buffer.Length)
        {
            if (offset + lengthSize > buffer.Length)
            {
                return false;
            }

            var nalSize = ReadLengthPrefix(buffer, offset, lengthSize);
            offset += lengthSize;
            if (nalSize <= 0 || offset + nalSize > buffer.Length)
            {
                return false;
            }

            output.Write([0, 0, 0, 1]);
            output.Write(buffer, offset, nalSize);
            offset += nalSize;
        }

        converted = output.ToArray();
        return converted.Length > 0;
    }

    /// <summary>
    /// 读取长度前缀的整数值
    /// </summary>
    /// <param name="buffer">源缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="lengthSize">长度前缀字节数</param>
    /// <returns>NAL 实际长度</returns>
    private static int ReadLengthPrefix(byte[] buffer, int offset, int lengthSize)
    {
        return lengthSize switch
        {
            1 => buffer[offset],
            2 => (buffer[offset] << 8) | buffer[offset + 1],
            3 => (buffer[offset] << 16) | (buffer[offset + 1] << 8) | buffer[offset + 2],
            _ => (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]
        };
    }
}
