using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AYLink.Core.Scrcpy;

// 音频编码枚举
public enum AudioCodec
{
    AAC,
    OPUS,
    FLAC,
    RAW
}

public unsafe class AudioDecoder(Socket audioSocket, bool handshake) : IDisposable
{
    private const int DeviceMetaLengthWithDummyByte = 65;
    private const ulong PacketFlagConfig = 1UL << 62;
    private readonly Socket _socket = audioSocket ?? throw new ArgumentNullException(nameof(audioSocket));
    private AVCodecContext* _codecContext;
    private SwrContext* _resamplerCtx;
    private AudioCodec _currentCodec;
    private readonly bool _handshake = handshake;

    public const int TARGET_SAMPLE_RATE = 48000; // 按理说可以动态处理节省计算量但是其实没必要
    public const int TARGET_CHANNELS = 2;
    private const AVSampleFormat TARGET_SAMPLE_FORMAT = AVSampleFormat.AV_SAMPLE_FMT_S16;

    /// <summary>
    /// 当音频数据被解码为 PCM 格式时触发
    /// 参数: pcmData
    /// </summary>
    public event Action<byte[]>? OnAudioDataDecoded;

    private void Handshake()
    {
        try
        {
            // For the first stream socket, scrcpy sends one dummy byte, then a fixed 64-byte device-name field.
            byte[] deviceHeader = ReceiveExact(DeviceMetaLengthWithDummyByte);
            string deviceName = System.Text.Encoding.UTF8.GetString(deviceHeader[1..]).TrimStart((char)0).Split('\0')[0];
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

            if (_currentCodec == AudioCodec.AAC)
            {
                InitializeAacDecoder();
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

    private void InitializeAacDecoder()
    {
        byte[] firstHeader = ReceiveExact(12);
        int firstPacketSize = ReadPacketSize(firstHeader);

        if (firstPacketSize <= 0)
        {
            throw new Exception("Invalid AAC packet size.");
        }

        byte[] firstPacketData = ReceiveExact(firstPacketSize);

        if (!IsConfigPacket(firstHeader))
        {
            throw new Exception("Expected AAC config packet, but got a data packet.");
        }

        _codecContext->extradata_size = firstPacketSize;
        _codecContext->extradata = (byte*)ffmpeg.av_malloc((ulong)firstPacketSize + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
        Marshal.Copy(firstPacketData, 0, (nint)_codecContext->extradata, firstPacketSize);
        Debug.WriteLine($"AAC config packet received, extradata size: {firstPacketSize}");
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
                int dataSize = ReadPacketSize(header);

                if (dataSize <= 0) continue;

                byte[] packetData = ReceiveExact(dataSize);
                DecodePacket(pkt, frame, packetData);
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&pkt);
            ffmpeg.av_frame_free(&frame);
        }
    }

    private void DecodePacket(AVPacket* pkt, AVFrame* frame, byte[] packetData)
    {
        int dataSize = packetData.Length;
        fixed (byte* pPacketData = packetData)
        {
            ffmpeg.av_new_packet(pkt, dataSize);
            Buffer.MemoryCopy(pPacketData, pkt->data, pkt->size, dataSize);

            int ret = ffmpeg.avcodec_send_packet(_codecContext, pkt);
            ffmpeg.av_packet_unref(pkt);
            if (ret < 0)
            {
                Debug.WriteLine($"avcodec_send_packet failed with error code: {ret}");
                return;
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
                
                OnAudioDataDecoded?.Invoke(managedBuffer);
            }

            ffmpeg.av_freep(&resampledData);
            ffmpeg.av_frame_unref(frame);
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
            OnAudioDataDecoded?.Invoke(pcmBytes);
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

    private static int ReadPacketSize(byte[] header)
    {
        byte[] sizeBytes = new byte[4];
        Array.Copy(header, 8, sizeBytes, 0, 4);
        Array.Reverse(sizeBytes);
        return BitConverter.ToInt32(sizeBytes, 0);
    }

    private static bool IsConfigPacket(byte[] header)
    {
        ulong ptsAndFlags = ReadUInt64BigEndian(header);
        return (ptsAndFlags & PacketFlagConfig) != 0;
    }

    private static ulong ReadUInt64BigEndian(byte[] buffer)
    {
        byte[] bytes = new byte[8];
        Array.Copy(buffer, 0, bytes, 0, 8);
        Array.Reverse(bytes);
        return BitConverter.ToUInt64(bytes, 0);
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
            catch (SocketException ex)
            {
                throw new EndOfStreamException($"Socket exception during receive: {ex.Message}", ex);
            }
        }
        
        if (offset < length)
        {
            throw new EndOfStreamException($"Incomplete read: expected {length} bytes, but got {offset} bytes.");
        }
        
        return buffer;
    }

    public void Dispose()
    {
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
