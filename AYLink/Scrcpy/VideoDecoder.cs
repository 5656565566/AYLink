using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;

namespace AYLink.Scrcpy;

// 视频编解码器枚举
public enum VideoCodec
{
    H264,
    H265,
    AV1
}

public unsafe class VideoDecoder : IDisposable
{
    private AVCodecContext* _codecContext;
    private AVCodecParserContext* _parserCtx;
    private SwsContext* _swsCtx;
    private AVFrame* _frame;
    private Socket _socket;
    private VideoCodec _selectedCodec;

    private int _lastWidth = 0;
    private int _lastHeight = 0;
    private AVPixelFormat _lastPixFmt = AVPixelFormat.AV_PIX_FMT_NONE;

    private WriteableBitmap? _writeableBitmap;
    private readonly Image _videoImage;

    public VideoDecoder(Socket socket, Image videoImage)
    {
        Debug.WriteLine($"FFmpeg version: {ffmpeg.av_version_info()}\n");
        _socket = socket;
        _videoImage = videoImage;
        _frame = ffmpeg.av_frame_alloc();
    }
    
    private void Handshake()
    {
        try
        {
            byte[] deviceHeader = ReceiveExact(65); // 长度为 64 的 String 但是尾部有 \0
            string deviceName = System.Text.Encoding.UTF8.GetString(deviceHeader).TrimStart((char)0).Split('\0')[0];
            Debug.WriteLine($"Device Name: {deviceName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to read device header: {ex.Message}");
            return;
        }

        try
        {
            byte[] codecBytes = ReceiveExact(4);

            _selectedCodec = codecBytes switch
            {
                [0x68, 0x32, 0x36, 0x34] => VideoCodec.H264,   // "h264"
                [0x68, 0x32, 0x36, 0x35] => VideoCodec.H265,   // "h265" 
                [0x00, 0x61, 0x76, 0x31] => VideoCodec.AV1,    // "av1\0"
                _ => throw new NotSupportedException($"Unsupported audio codec ID: {BitConverter.ToString(codecBytes)}")
            };
            Debug.WriteLine($"Codec: {BitConverter.ToString(codecBytes)}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to read video meta: {ex.Message}");
            return;
        }

        try
        {
            byte[] widthBytes = ReceiveExact(4);
            byte[] heightBytes = ReceiveExact(4);
            int width = BitConverter.ToInt32([.. widthBytes.Reverse()], 0);
            int height = BitConverter.ToInt32([.. heightBytes.Reverse()], 0);

            if (width <= 0 || width > 8000 || height <= 0 || height > 8000)
            {
                throw new InvalidOperationException($"Parsed unreasonable dimensions: {width}x{height}");
            }
            Debug.WriteLine($"Initial Dimensions: {width} x {height}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to read video dimensions after sync: {ex.Message}");
            return;
        }
    }

    // 根据选择的编解码器进行初始化
    public void InitializeFromStream()
    {
        Handshake();

        AVCodecID codecId = GetCodecId(_selectedCodec);

        AVCodec* codec = ffmpeg.avcodec_find_decoder(codecId);
        if (codec == null)
            throw new InvalidOperationException($"{_selectedCodec} codec not found");

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
            throw new OutOfMemoryException("Failed to allocate codec context");

        _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        
        _codecContext->thread_count = 0; // 低延迟可以用 1
        _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME; // 帧级并行

        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            throw new InvalidOperationException("Failed to open codec");

        _parserCtx = ffmpeg.av_parser_init((int)codecId);
        if (_parserCtx == null)
            throw new InvalidOperationException("Failed to init parser");
    }

    // 根据枚举获取 FFmpeg 的 AVCodecID
    private static AVCodecID GetCodecId(VideoCodec codec)
    {
        switch (codec)
        {
            case VideoCodec.H264:
                return AVCodecID.AV_CODEC_ID_H264;
            case VideoCodec.H265:
                return AVCodecID.AV_CODEC_ID_HEVC;
            case VideoCodec.AV1:
                return AVCodecID.AV_CODEC_ID_AV1;
            default:
                throw new ArgumentOutOfRangeException(nameof(codec), "Unsupported codec");
        }
    }

    public void PlayH264orHEVC()
    {
        // 增加缓冲区大小以减少 socket receive 调用次数
        byte[] buffer = new byte[1024 * 64];
        AVPacket* packet = ffmpeg.av_packet_alloc();
        if (packet == null) throw new OutOfMemoryException("Failed to allocate AVPacket");

        try
        {
            while (_socket.Connected)
            {
                int bytesRead;
                try
                {
                    bytesRead = _socket.Receive(buffer);
                }
                catch (SocketException)
                {
                    break;
                }

                if (bytesRead <= 0) break;

                fixed (byte* p = buffer)
                {
                    byte* dataPtr = p;
                    int dataSize = bytesRead;

                    while (dataSize > 0)
                    {
                        byte* outData;
                        int outSize;
                        int len = ffmpeg.av_parser_parse2(
                            _parserCtx, _codecContext,
                            &outData, &outSize,
                            dataPtr, dataSize,
                            ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);

                        if (len < 0)
                        {
                            Debug.WriteLine("Error while parsing.");
                            dataSize = 0;
                            break;
                        }

                        dataPtr += len;
                        dataSize -= len;

                        if (outSize > 0)
                        {
                            packet->data = outData;
                            packet->size = outSize;
                            DecodePacket(packet);
                        }
                    }
                }
            }

            // 刷新解码器
            DecodePacket(null);
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            if (_parserCtx != null)
            {
                ffmpeg.av_parser_close(_parserCtx);
                _parserCtx = null;
            }
        }
    }
    private byte[] ReceiveExact(int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            try
            {
                int received = _socket.Receive(buffer, offset, length - offset, SocketFlags.None);
                if (received == 0) throw new EndOfStreamException("Socket closed prematurely.");
                offset += received;
            }
            catch (SocketException ex)
            {
                throw new IOException("Socket error while receiving data.", ex);
            }
        }
        return buffer;
    }

    private void PlayAV1() // 未经测试 因为我没设备支持（
    {
        AVPacket* packet = ffmpeg.av_packet_alloc();
        if (packet == null) throw new OutOfMemoryException("Failed to allocate AVPacket");

        try
        {
            while (_socket.Connected)
            {
                byte[] ptsBytes = ReceiveExact(8);
                byte[] sizeBytes = ReceiveExact(4);
                int packetSize = BitConverter.ToInt32([.. sizeBytes.Reverse()], 0);
                long pts = BitConverter.ToInt64([.. ptsBytes.Reverse()], 0);

                // --- 正常数据包处理 ---
                if (packetSize <= 0 || packetSize > 2 * 1024 * 1024)
                {
                    Debug.WriteLine($"Skipping packet with invalid size: {packetSize}");
                    continue;
                }

                if (packetSize == 4)
                {
                    ReceiveExact(packetSize);
                }

                byte[] frameBuffer = ReceiveExact(packetSize);

                // 处理正常数据包
                if (ffmpeg.av_new_packet(packet, packetSize) < 0)
                {
                    Debug.WriteLine("Failed to allocate new packet buffer.");
                    continue;
                }

                System.Runtime.InteropServices.Marshal.Copy(frameBuffer, 0, (nint)packet->data, packetSize);
                packet->pts = pts;

                DecodePacket(packet);
                ffmpeg.av_packet_unref(packet);
            }
        }
        finally
        {
            if (_codecContext != null && _codecContext->extradata != null)
            {
                ffmpeg.av_freep(&_codecContext->extradata);
            }
            ffmpeg.av_packet_free(&packet);
        }
    }

    public void Start()
    {
        if (_selectedCodec == VideoCodec.H264 || _selectedCodec == VideoCodec.H265)
        {
            PlayH264orHEVC();
            return;
        }

        if (_selectedCodec == VideoCodec.AV1)
        {
            PlayAV1();
            return;
        }
    }

    private void DecodePacket(AVPacket* packet)
    {
        int ret = ffmpeg.avcodec_send_packet(_codecContext, packet);
        if (ret < 0)
        {
            // 对于 flushing，发送 null packet 后会返回 EOF 是正常的
            if (ret != ffmpeg.AVERROR_EOF && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                Debug.WriteLine($"Failed to send packet to decoder: {ret}");
            }
            // 即便发送失败，也尝试去接收帧，因为解码器内部可能还有缓存的帧
        }

        while (true)
        {
            ret = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
            {
                break; // 需要更多数据包或已到达流末尾
            }
            if (ret < 0)
            {
                Debug.WriteLine($"Failed to receive frame from decoder: {ret}");
                break; // 出现错误，跳出循环
            }

            try
            {
                UpdateBitmapFromFrame(_frame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bitmap conversion or event invocation error: {ex.Message}");
            }
            finally
            {
                ffmpeg.av_frame_unref(_frame);
            }
        }
    }

    /// <summary>
    /// 从解码后的AVFrame高效更新UI上的WriteableBitmap。
    /// 这个方法应该在解码线程中被调用。
    /// </summary>
    /// <param name="frame">来自FFmpeg解码器的AVFrame指针</param>
    public unsafe void UpdateBitmapFromFrame(AVFrame* frame)
    {
        int width = frame->width;
        int height = frame->height;
        var pix_fmt = (AVPixelFormat)frame->format;

        if (width <= 0 || height <= 0) return; // 无效帧，直接忽略

        if (_writeableBitmap == null || width != _lastWidth || height != _lastHeight || pix_fmt != _lastPixFmt)
        {
            // 释放旧的SwsContext
            if (_swsCtx != null)
            {
                ffmpeg.sws_freeContext(_swsCtx);
            }

            _swsCtx = ffmpeg.sws_getContext(
                width, height, pix_fmt,
                width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                ffmpeg.SWS_BILINEAR, null, null, null);

            if (_swsCtx == null)
                throw new InvalidOperationException("Failed to create SwsContext for frame conversion.");

            // 更新缓存的尺寸和格式信息
            _lastWidth = width;
            _lastHeight = height;
            _lastPixFmt = pix_fmt;

            Dispatcher.UIThread.Invoke(() =>
            {
                _writeableBitmap = new WriteableBitmap(
                    new Avalonia.PixelSize(width, height),
                    new Avalonia.Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Opaque);

                // 更新Image控件的Source
                _videoImage.Source = _writeableBitmap;
            });
        }

        using (var frameBuffer = _writeableBitmap!.Lock())
        {
            // 准备sws_scale的目标参数
            byte_ptrArray4 dstData = default;
            int_array4 dstLinesize = default;
            dstData[0] = (byte*)frameBuffer.Address;
            dstLinesize[0] = frameBuffer.RowBytes;

            ffmpeg.sws_scale(
                _swsCtx,
                frame->data, frame->linesize,
                0, height,
                dstData, dstLinesize);
        }

        // 通知UI刷新
        Dispatcher.UIThread.Post(() => _videoImage.InvalidateVisual());
    }

    public void Dispose()
    {
        _socket?.Close();
        _socket?.Dispose();

        if (_frame != null)
        {
            fixed (AVFrame** ptr = &_frame)
            {
                ffmpeg.av_frame_free(ptr);
                _frame = null;
            }
        }

        if (_codecContext != null)
        {
            fixed (AVCodecContext** ptr = &_codecContext)
            {
                ffmpeg.avcodec_free_context(ptr);
                _codecContext = null;
            }
        }

        if (_swsCtx != null)
        {
            ffmpeg.sws_freeContext(_swsCtx);
            _swsCtx = null;
        }

        GC.SuppressFinalize(this);
    }
}