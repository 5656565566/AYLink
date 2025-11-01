using AYLink.Utils;
using FFmpeg.AutoGen;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AYLink.Scrcpy;

// 音频编码枚举
enum AudioCodec
{
    AAC,
    OPUS,
    FLAC,
    RAW
}

public unsafe class AudioDecoder : IDisposable
{
    private readonly Socket _socket;
    private AVCodecContext* _codecContext;
    private SwrContext* _resamplerCtx;
    private AudioCodec _currentCodec;
    private int _streamId = 0;
    private readonly AudioPlayer _player = AudioPlayer.Instance;
    private readonly bool _handshake;

    private const int TARGET_SAMPLE_RATE = 48000; // 按理说可以动态处理节省计算量但是其实没必要
    private const int TARGET_CHANNELS = 2;
    private const AVSampleFormat TARGET_SAMPLE_FORMAT = AVSampleFormat.AV_SAMPLE_FMT_S16;

    public AudioDecoder(Socket audioSocket, bool handshake)
    {
        _handshake = handshake;
        _socket = audioSocket ?? throw new ArgumentNullException(nameof(audioSocket));
        _streamId = _player.StreamPlayStart(TARGET_SAMPLE_RATE, TARGET_CHANNELS);
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
    }

    public void Start()
    {
        if (_handshake)
        {
            Handshake();
        }

        try
        {
            byte[] codecIdBytes = ReceiveExact(4);

            AVCodecID avCodecId = codecIdBytes switch
            {
                [0x6F, 0x70, 0x75, 0x73] => AVCodecID.AV_CODEC_ID_OPUS,
                [0x00, 0x61, 0x61, 0x63] => AVCodecID.AV_CODEC_ID_AAC,
                [0x66, 0x6C, 0x61, 0x63] => AVCodecID.AV_CODEC_ID_FLAC,
                [0x00, 0x72, 0x61, 0x77] => AVCodecID.AV_CODEC_ID_PCM_S16LE,
                _ => throw new NotSupportedException($"Unsupported audio codec ID: {BitConverter.ToString(codecIdBytes)}")
            };

            _currentCodec = codecIdBytes switch
            {
                [0x6F, 0x70, 0x75, 0x73] => AudioCodec.OPUS,
                [0x00, 0x61, 0x61, 0x63] => AudioCodec.AAC,
                [0x66, 0x6C, 0x61, 0x63] => AudioCodec.FLAC,
                [0x00, 0x72, 0x61, 0x77] => AudioCodec.RAW,
                _ => throw new NotSupportedException($"Unsupported audio codec ID: {BitConverter.ToString(codecIdBytes)}")
            };

            Debug.WriteLine($"Audio codec: {BitConverter.ToString(codecIdBytes)}");

            if (_currentCodec == AudioCodec.RAW)
            {
                PlaybackLoop();
                return;
            }

            var codec = ffmpeg.avcodec_find_decoder(avCodecId);
            if (codec == null)
                throw new Exception($"FFmpeg codec not found for: {avCodecId}");

            _codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (_codecContext == null)
                throw new Exception("Failed to allocate codec context");

            if (_currentCodec == AudioCodec.AAC) // 必须为 aac 做 extradata 配置 ！！！（教训，别信ai文档
            {
                byte[] configHeader = ReceiveExact(12);

                if ((configHeader[0] & 0x80) == 0)
                {
                    throw new Exception("Expected AAC config packet, but got a data packet.");
                }

                byte[] sizeBytes = new byte[4];
                Array.Copy(configHeader, 8, sizeBytes, 0, 4);
                Array.Reverse(sizeBytes);
                int configDataSize = BitConverter.ToInt32(sizeBytes, 0);

                if (configDataSize <= 0)
                {
                    throw new Exception("Invalid AAC config packet size.");
                }

                byte[] configData = ReceiveExact(configDataSize);

                _codecContext->extradata_size = configDataSize;
                _codecContext->extradata = (byte*)ffmpeg.av_malloc((ulong)configDataSize + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
                Marshal.Copy(configData, 0, (nint)_codecContext->extradata, configDataSize);
            }

            if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
                throw new Exception("Failed to open codec");

            Debug.WriteLine($"Audio codec: {_currentCodec}, ready to play.");
            PlaybackLoop();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Audio initialization or playback failed: {ex}");
            Dispose();
        }
    }

    private void InitializeResamplerFromFrame(AVFrame* frame)
    {
        AVChannelLayout chLayoutIn = new(), chLayoutOut = new();
        ffmpeg.av_channel_layout_copy(&chLayoutIn, &frame->ch_layout);
        ffmpeg.av_channel_layout_default(&chLayoutOut, TARGET_CHANNELS);

        fixed (SwrContext** ctx = &_resamplerCtx)
        {
            ffmpeg.swr_alloc_set_opts2(
                ctx,
                &chLayoutOut,
                TARGET_SAMPLE_FORMAT,
                TARGET_SAMPLE_RATE,
                &chLayoutIn,
                (AVSampleFormat)frame->format,
                frame->sample_rate,
                0, null
            );
        }

        int status = ffmpeg.swr_init(_resamplerCtx);
        if (status < 0)
        {
            byte* buffer = stackalloc byte[256];
            ffmpeg.av_strerror(status, buffer, 256);
            string? errorMessage = Marshal.PtrToStringAnsi((nint)buffer);
            fixed (SwrContext** ptr = &_resamplerCtx) { ffmpeg.swr_free(ptr); }
            throw new Exception($"Failed to initialize the resampling context: {errorMessage} (code {status})");
        }

        ffmpeg.av_channel_layout_uninit(&chLayoutIn);
        ffmpeg.av_channel_layout_uninit(&chLayoutOut);
    }

    private void PlayDecodable()
    {
        var pkt = ffmpeg.av_packet_alloc();
        var frame = ffmpeg.av_frame_alloc();
        try
        {
            while (_socket.Connected)
            {
                byte[] header = ReceiveExact(12);
                byte[] sizeBytes = new byte[4];
                Array.Copy(header, 8, sizeBytes, 0, 4);
                Array.Reverse(sizeBytes);
                int dataSize = BitConverter.ToInt32(sizeBytes, 0);

                if (dataSize <= 0) continue;

                byte[] packetData = ReceiveExact(dataSize);

                fixed (byte* pPacketData = packetData)
                {
                    ffmpeg.av_new_packet(pkt, dataSize);
                    Buffer.MemoryCopy(pPacketData, pkt->data, pkt->size, dataSize);

                    int ret = ffmpeg.avcodec_send_packet(_codecContext, pkt);
                    ffmpeg.av_packet_unref(pkt);
                    if (ret < 0)
                    {
                        // 可以在这里加一个断点或日志来捕获错误码
                        Debug.WriteLine($"avcodec_send_packet failed with error code: {ret}");
                        continue;
                    }
                }

                while (ffmpeg.avcodec_receive_frame(_codecContext, frame) == 0)
                {
                    if (_resamplerCtx == null)
                    {
                        InitializeResamplerFromFrame(frame);
                    }

                    byte* resampledData;
                    ffmpeg.av_samples_alloc(&resampledData, null, TARGET_CHANNELS, frame->nb_samples, TARGET_SAMPLE_FORMAT, 0);

                    int outSamples = ffmpeg.swr_convert(
                        _resamplerCtx,
                        &resampledData,
                        frame->nb_samples,
                        frame->extended_data,
                        frame->nb_samples
                    );

                    if (outSamples > 0)
                    {
                        int bufferSize = ffmpeg.av_samples_get_buffer_size(null, TARGET_CHANNELS, outSamples, TARGET_SAMPLE_FORMAT, 1);
                        byte[] managedBuffer = new byte[bufferSize];
                        Marshal.Copy((nint)resampledData, managedBuffer, 0, bufferSize);
                        _player.StreamPush(_streamId, managedBuffer);
                    }

                    ffmpeg.av_freep(&resampledData);
                    ffmpeg.av_frame_unref(frame);
                }
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&pkt);
            ffmpeg.av_frame_free(&frame);
        }
    }

    private void PlayRAW()
    {
        while (_socket.Connected)
        {
            ReceiveExact(8);
            byte[] sizeBytes = ReceiveExact(4);
            Array.Reverse(sizeBytes);
            int dataSize = BitConverter.ToInt32(sizeBytes, 0);
            if (dataSize == 0) continue;

            byte[] pcmBytes = ReceiveExact(dataSize);
            _player.StreamPush(_streamId, pcmBytes);
        }
    }

    private void PlaybackLoop()
    {
        if (_currentCodec == AudioCodec.RAW)
        {
            PlayRAW();
        }
        else
        {
            PlayDecodable();
        }
    }

    private byte[] ReceiveExact(int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length && _socket.Connected)
        {
            try
            {
                int received = _socket.Receive(buffer, offset, length - offset, SocketFlags.None);
                if (received == 0) throw new EndOfStreamException("Socket closed prematurely.");
                offset += received;
            }
            catch (SocketException)
            {
                break;
            }
        }
        return buffer;
    }

    public void Dispose()
    {
        // _player.Stop();
        _socket?.Close();

        if (_resamplerCtx != null)
        {
            var resamplerCtx = _resamplerCtx;
            ffmpeg.swr_free(&resamplerCtx);
            _resamplerCtx = null;
        }

        if (_codecContext != null)
        {
            if (_codecContext->extradata != null)
            {
                ffmpeg.av_freep(&_codecContext->extradata);
            }

            var context = _codecContext;
            ffmpeg.avcodec_free_context(&context);
            _codecContext = null;
        }

        _socket?.Dispose();
        GC.SuppressFinalize(this);
    }
}