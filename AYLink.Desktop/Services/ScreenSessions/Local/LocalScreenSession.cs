using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Services.Input;
using Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.Services.ScreenSessions.Local;

/// <summary>
/// 面向页面层的本地投屏会话门面
/// </summary>
internal sealed class LocalScreenSession : IScreenSession, IResizableScreenSession
{
    private readonly LocalScreenAudioOutput _audioOutput;
    private readonly LocalScreenControlBridge _controlBridge = new();
    private readonly DeviceModel _device;
    private readonly string? _appPackageName;
    private LocalScrcpySessionRuntime? _runtime;
    private bool _disposed;

    public ScreenSessionState State { get; private set; } = ScreenSessionState.Idle;
    public bool IsFlexDisplayEnabled => _runtime?.IsFlexDisplayEnabled == true;
    public ScrcpyClient? Client => _runtime?.Client;

    public event Action<ScreenSessionState>? StateChanged;
    public event Action<int, int, IntPtr, int>? VideoFrameDecoded;
    public event Action<Exception>? SessionError;

    public LocalScreenSession(DeviceModel device, string? appPackageName, AudioPlayer audioPlayer)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _appPackageName = appPackageName;
        _audioOutput = new LocalScreenAudioOutput(audioPlayer);
    }

    public async Task StartAsync(IInputProcessor inputProcessor, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(inputProcessor);

        if (_runtime != null)
        {
            return;
        }

        UpdateState(ScreenSessionState.Connecting);
        _runtime = new LocalScrcpySessionRuntime(_device, _appPackageName, _audioOutput, _controlBridge);
        _runtime.VideoFrameDecoded += HandleVideoFrameDecoded;

        try
        {
            await _runtime.ConnectAsync(inputProcessor);
            UpdateState(ScreenSessionState.Connected);
        }
        catch (Exception ex)
        {
            SessionError?.Invoke(ex);
            UpdateState(ScreenSessionState.Faulted);
            CleanupRuntime();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        CleanupRuntime();
        UpdateState(ScreenSessionState.Closed);
        return Task.CompletedTask;
    }

    public void SendControl(byte[] payload)
    {
        _controlBridge.SendControl(payload);
    }

    public void SendPointerMove(byte[] payload)
    {
        _controlBridge.SendPointerMove(payload);
    }

    public bool SendResizeDisplayIfNeeded(Size newSize, bool hasReceivedFirstVideoFrame, Size? lastResizeRequestSize)
    {
        return _controlBridge.SendResizeDisplayIfNeeded(
            newSize,
            IsFlexDisplayEnabled,
            _runtime?.CanResizeDisplay == true,
            hasReceivedFirstVideoFrame,
            lastResizeRequestSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CleanupRuntime();
        _audioOutput.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void CleanupRuntime()
    {
        if (_runtime != null)
        {
            _runtime.VideoFrameDecoded -= HandleVideoFrameDecoded;
            _runtime.Dispose();
            _runtime = null;
        }
    }

    private void HandleVideoFrameDecoded(int width, int height, IntPtr bgraDataPtr, int rowBytes)
    {
        VideoFrameDecoded?.Invoke(width, height, bgraDataPtr, rowBytes);
    }

    private void UpdateState(ScreenSessionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }
}
