using SDL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static SDL.SDL3;

namespace AYLink.Utils;


/// <summary>
/// 一个支持多音源混音和音频设备热切换的音频播放器
/// 它管理一个音频设备，并将所有活动的音频源混合在一起进行播放
/// </summary>
public sealed unsafe class AudioPlayer : IDisposable
{
    private static readonly Lazy<AudioPlayer> _instance = new(() => new AudioPlayer());
    public static AudioPlayer Instance => _instance.Value;

    private const int TARGET_SAMPLE_RATE = 48000;
    private const SDL_AudioFormat TARGET_AUDIO_FORMAT = SDL_AudioFormat.SDL_AUDIO_S16LE;
    private const int TARGET_CHANNELS = 2;

    private readonly object _deviceLock = new();

    private SDL_AudioDeviceID _audioDevice = 0;
    private SDL_AudioStream* _masterAudioStream = null;
    private SDL_AudioSpec _targetSpec;

    private readonly ConcurrentDictionary<int, AudioSource> _sources = new();
    private int _nextSourceId = 1;
    private Thread? _mixerThread;
    private volatile bool _shutdownRequested = false; // volatile 确保多线程可见性
    private float _globalVolume = 1.0f;

    private bool _disposed = false;
    public bool IsAudioDeviceAvailable => _audioDevice != 0;

    private AudioPlayer()
    {
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO))
        {
            throw new Exception($"SDL_Init failed: {SDL_GetError()}");
        }

        SDL_SetHint(SDL_HINT_AUDIO_DEVICE_STREAM_ROLE, "game"u8);
    }

    /// <summary>
    /// 是否可以播放声音
    /// </summary>
    public bool IsActivate()
    {
        return _mixerThread != null && _mixerThread.IsAlive;
    }

    /// <summary>
    /// 配置要使用的音频播放设备。支持运行时热切换。
    /// </summary>
    public void ConfigureAudioDevice(string? deviceName = null)
    {
        lock (_deviceLock)
        {
            SDL_AudioDeviceID oldDevice = _audioDevice;
            SDL_AudioStream* oldMasterStream = _masterAudioStream;

            SDL_AudioDeviceID selectedDeviceID = SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK;
            if (!string.IsNullOrEmpty(deviceName))
            {
                var device = GetPlaybackDevices().FirstOrDefault(d => d.Name == deviceName);
                if (device != default)
                {
                    selectedDeviceID = device.InstanceID;
                }
                else
                {
                    Debug.WriteLine($"Warning: Audio device '{deviceName}' not found. Using default.");
                }
            }

            _targetSpec = new SDL_AudioSpec
            {
                freq = TARGET_SAMPLE_RATE,
                format = TARGET_AUDIO_FORMAT,
                channels = TARGET_CHANNELS,
            };

            var spec = _targetSpec;
            _audioDevice = SDL_OpenAudioDevice(selectedDeviceID, &spec);

            if (_audioDevice == 0)
            {
                Debug.WriteLine($"Failed to open audio device: {SDL_GetError()}");
                _audioDevice = oldDevice;
                _masterAudioStream = oldMasterStream;
                return; // 切换失败，提前退出
            }

            _masterAudioStream = SDL_CreateAudioStream(&spec, &spec);
            if (_masterAudioStream == null)
            {
                SDL_CloseAudioDevice(_audioDevice);
                _audioDevice = oldDevice; // 恢复旧设备ID
                _masterAudioStream = oldMasterStream; // 恢复旧流
                throw new Exception($"Failed to create master audio stream: {SDL_GetError()}");
            }

            if (!SDL_BindAudioStream(_audioDevice, _masterAudioStream))
            {
                SDL_DestroyAudioStream(_masterAudioStream);
                SDL_CloseAudioDevice(_audioDevice);
                _audioDevice = oldDevice; // 恢复旧设备ID
                _masterAudioStream = oldMasterStream; // 恢复旧流
                throw new Exception($"Failed to bind master audio stream: {SDL_GetError()}");
            }

            if (oldDevice != 0)
            {
                Debug.WriteLine("Cleaning up old audio device.");
                SDL_UnbindAudioStream(oldMasterStream);
                SDL_DestroyAudioStream(oldMasterStream);
                SDL_CloseAudioDevice(oldDevice);
            }

            // --- 仅在第一次配置时启动混音线程 ---
            if (_mixerThread == null)
            {
                _mixerThread = new Thread(MixerLoop) { IsBackground = true, Name = "AudioMixerThread" };
                _mixerThread.Start();
            }

            // --- 激活新设备 ---
            SDL_ResumeAudioDevice(_audioDevice);
            Debug.WriteLine("Audio device configured/switched successfully.");
        }
    }


    /// <summary>
    /// 混音器主循环，在专用线程上运行。
    /// </summary>
    private void MixerLoop()
    {
        int bytesPerSample = TARGET_CHANNELS * 2; // S16LE = 2 bytes
        // 缓冲20毫秒的数据量，以减少延迟
        int bufferSize = (TARGET_SAMPLE_RATE * bytesPerSample * 20) / 1000;

        byte[] sourceBuffer = new byte[bufferSize];
        float[] mixBufferFloat = new float[bufferSize / 2]; // 每个样本2字节
        byte[] finalBuffer = new byte[bufferSize];

        while (!_shutdownRequested)
        {
            Array.Clear(mixBufferFloat, 0, mixBufferFloat.Length);
            bool hasActiveSources = false;

            // 遍历所有音源，获取转换后的数据并混合
            foreach (var source in _sources.Values)
            {
                int bytesRead = source.GetConvertedData(sourceBuffer);
                if (bytesRead > 0)
                {
                    hasActiveSources = true;
                    // 将 S16LE 字节数据叠加到浮点混音缓冲区
                    for (int i = 0; i < bytesRead / 2; i++)
                    {
                        short sample = (short)(sourceBuffer[i * 2] | (sourceBuffer[i * 2 + 1] << 8));
                        // 应用音源音量和全局音量
                        mixBufferFloat[i] += (sample / 32768.0f) * source.Volume * _globalVolume;
                    }
                }
            }

            if (hasActiveSources)
            {
                // 将浮点缓冲区转换回 S16LE 字节数据
                for (int i = 0; i < mixBufferFloat.Length; i++)
                {
                    // 削波处理
                    float sampleFloat = Math.Clamp(mixBufferFloat[i], -1.0f, 1.0f);
                    short finalSample = (short)(sampleFloat * short.MaxValue);
                    finalBuffer[i * 2] = (byte)(finalSample & 0xFF);
                    finalBuffer[i * 2 + 1] = (byte)(finalSample >> 8);
                }

                lock (_deviceLock)
                {
                    // 在推送前检查流是否有效（可能在切换过程中为null）
                    if (_masterAudioStream != null && !_shutdownRequested)
                    {
                        fixed (byte* p = finalBuffer)
                        {
                            SDL_PutAudioStreamData(_masterAudioStream, (nint)p, bufferSize);
                        }
                    }
                }
            }

            // 使用更精确的延迟来减少CPU占用
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// 为一个新的音频流做好播放准备。
    /// </summary>
    /// <param name="inputSampleRate">输入音频的采样率。</param>
    /// <param name="inputChannels">输入音频的声道数。</param>
    /// <returns>一个代表此音频流的唯一ID。</returns>
    public int StreamPlayStart(int inputSampleRate, int inputChannels)
    {
        if (_audioDevice == 0)
        {
            throw new InvalidOperationException("Audio device is not configured. Call ConfigureAudioDevice first.");
        }

        SDL_AudioSpec inputSpec = new()
        {
            freq = inputSampleRate,
            format = TARGET_AUDIO_FORMAT, // 假设输入格式与目标格式相同
            channels = (byte)inputChannels,
        };

        int sourceId = Interlocked.Increment(ref _nextSourceId);
        var newSource = new AudioSource(sourceId, inputSpec, _targetSpec);
        _sources.TryAdd(sourceId, newSource);

        return sourceId;
    }

    /// <summary>
    /// 将 PCM 音频数据推送到指定的播放流中。
    /// </summary>
    /// <param name="streamId">由 StreamPlayStart 返回的流ID。</param>
    /// <param name="pcmBytes">PCM 格式的字节数组 (S16LE)。</param>
    public void StreamPush(int streamId, byte[] pcmBytes)
    {
        if (_sources.TryGetValue(streamId, out var source))
        {
            source.PushData(pcmBytes);
        }
        else
        {
            Debug.WriteLine($"Warning: Attempted to push data to a non-existent stream ID: {streamId}");
        }
    }

    /// <summary>
    /// 停止并移除一个流式播放源。
    /// </summary>
    /// <param name="streamId">要停止的流ID。</param>
    public void StopStream(int streamId)
    {
        if (_sources.TryRemove(streamId, out var source))
        {
            source.Dispose();
        }
    }

    /// <summary>
    /// 停止所有流的播放。
    /// </summary>
    public void StopAllStreams()
    {
        foreach (var sourceId in _sources.Keys)
        {
            StopStream(sourceId);
        }
        _sources.Clear();

        lock (_deviceLock)
        {
            if (_masterAudioStream != null)
            {
                SDL_ClearAudioStream(_masterAudioStream);
            }
        }
    }

    /// <summary>
    /// 设置指定流的音量。
    /// </summary>
    /// <param name="streamId">流ID。</param>
    /// <param name="volume">音量值 (0.0f - 1.0f+)。</param>
    public void SetStreamVolume(int streamId, float volume)
    {
        if (_sources.TryGetValue(streamId, out var source))
        {
            source.Volume = Math.Max(0.0f, volume);
        }
    }

    /// <summary>
    /// 设置全局音量。
    /// </summary>
    /// <param name="volume">音量值 (0.0f - 1.0f+)。</param>
    public void SetGlobalVolume(float volume)
    {
        _globalVolume = Math.Max(0.0f, volume);
    }

    /// <summary>
    /// 获取可以播放的音频设备
    /// </summary>
    /// <returns>设备的列表</returns>
    public static List<(string Name, SDL_AudioDeviceID InstanceID)> GetPlaybackDevices()
    {
        var devices = new List<(string, SDL_AudioDeviceID)>();
        int count;

        SDL_AudioDeviceID* deviceIds = SDL_GetAudioPlaybackDevices(&count);
        if (deviceIds != null)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    string? name = SDL_GetAudioDeviceName(deviceIds[i]);
                    if (!string.IsNullOrEmpty(name))
                    {
                        devices.Add((name, deviceIds[i]));
                    }
                }
            }
            finally
            {
                SDL_free(deviceIds);
            }
        }
        return devices;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _shutdownRequested = true;
                _mixerThread?.Join(); // 等待混音线程安全退出

                StopAllStreams();

                lock (_deviceLock)
                {
                    if (_audioDevice != 0)
                    {
                        if (_masterAudioStream != null)
                        {
                            SDL_UnbindAudioStream(_masterAudioStream);
                            SDL_DestroyAudioStream(_masterAudioStream);
                            _masterAudioStream = null;
                        }
                        SDL_CloseAudioDevice(_audioDevice);
                        _audioDevice = 0;
                    }
                }
            }

            // 即使在析构函数中，也要确保SDL被正确关闭
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
            SDL_Quit();

            _disposed = true;
        }
    }

    ~AudioPlayer()
    {
        Dispose(false);
    }
}