using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Core.Scrcpy.Control;
using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Audio;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static AYLink.Core.Scrcpy.Control.ControlMsgModel;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 投屏标签页 ViewModel - 每个设备对应一个标签页
/// 管理 ScrcpyClient 连接、视频渲染和控制输入
/// </summary>
public partial class ScreenTabViewModel : TabItemViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private Avalonia.Media.Imaging.WriteableBitmap? _videoSource;

    public Services.Input.IInputProcessor InputProcessor { get; } = new Services.Input.DefaultInputProcessor();

    private ScrcpyClient? _scrcpyClient;
    private Size _screenSize;
    private readonly string? _appName;
    private bool _isPointerCaptured;
    private bool _disposed;
    private int _audioStreamId = -1;
    private readonly AudioPlayer _audioPlayer = AudioPlayer.Instance;

    /// <summary>
    /// 视频 Image 控件引用
    /// </summary>
    private Image? _videoImage;

    public ScreenTabViewModel(DeviceModel device, string? appName = null)
    {
        Device = device;
        _appName = appName;
        Title = appName != null ? $"{device.Name} - {appName}" : device.Name;
    }

    /// <summary>
    /// 绑定视频显示控件
    /// 首次绑定时启动连接；重排/重新挂载时恢复引用并重新绑定输入事件
    /// </summary>
    public void AttachVideoImage(Image videoImage)
    {
        // 如果是同一个实例 无需处理
        if (ReferenceEquals(_videoImage, videoImage)) return;

        // 先解绑旧控件的事件（如果有旧引用）
        DetachEventHandlers();

        _videoImage = videoImage;

        if (Device != null && !IsConnected)
        {
            // 首次连接
            _ = ConnectDeviceAsync();
        }
        else if (IsConnected)
        {
            // 已连接状态下重新挂载（重排/视图切换）- 重新绑定输入事件
            SetupEventHandlers();
        }
    }

    /// <summary>
    /// 分离视频显示控件引用
    /// 仅解绑事件并置空引用
    /// 避免重排标签页时导致后端中断
    /// 事件的真正清理和后端停止在 Dispose 时执行
    /// </summary>
    public void DetachVideoImage()
    {
        DetachEventHandlers();
        _videoImage = null;
    }

    /// <summary>
    /// 关闭标签页 - 先释放资源再触发关闭事件
    /// </summary>
    protected override void CloseTab()
    {
        Dispose();
        base.CloseTab();
    }

    /// <summary>
    /// 连接设备并开始投屏
    /// </summary>
    private async Task ConnectDeviceAsync()
    {
        if (Device?.AdbClient == null || _videoImage == null) return;

        try
        {
            _scrcpyClient = new ScrcpyClient(Device);
            
            // 订阅视频帧解码事件
            _scrcpyClient.OnVideoFrameDecoded += (width, height, bgraDataPtr, rowBytes) =>
            {
                // 在回调中检查 _disposed 防止访问已释放的内存导致 AccessViolationException
                if (_disposed || _videoImage == null) return;

                Dispatcher.UIThread.Post(() =>
                {
                    // 再次检查 - Post 到 UI 线程执行时可能已经被 Dispose
                    if (_disposed || _videoImage == null) return;

                    try
                    {
                        if (VideoSource == null || VideoSource.PixelSize.Width != width || VideoSource.PixelSize.Height != height)
                        {
                            var oldBitmap = VideoSource;
                            VideoSource = new Avalonia.Media.Imaging.WriteableBitmap(
                                new PixelSize(width, height),
                                new Vector(96, 96),
                                Avalonia.Platform.PixelFormat.Bgra8888,
                                Avalonia.Platform.AlphaFormat.Premul);
                            oldBitmap?.Dispose();
                        }

                        using (var buf = VideoSource.Lock())
                        {
                            unsafe
                            {
                                Buffer.MemoryCopy(
                                    bgraDataPtr.ToPointer(),
                                    buf.Address.ToPointer(),
                                    buf.RowBytes * height,
                                    rowBytes * height);
                            }
                        }
                        
                        // 触发 UI 更新
                        _videoImage?.InvalidateVisual();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ScreenTab] Render frame error: {ex.Message}");
                    }
                }, DispatcherPriority.Render);
            };

            // 订阅音频解码事件 - 将解码后的 PCM 数据推送到音频播放器
            _scrcpyClient.OnAudioDataDecoded += (pcmData) =>
            {
                if (_audioStreamId >= 0)
                {
                    _audioPlayer.StreamPush(_audioStreamId, pcmData);
                }
            };

            await Task.Run(async () =>
            {
                ScrcpyTool tool = ScrcpyService.Instance.Tool;
                var displays = tool.GetResolutions(Device);

                // 加载设备配置并应用到 ServerOptions
                var deviceConfig = ConfigManager.Instance.LoadConfig<DeviceConfig>(
                    HashHelper.ToMd5Hash(Device.Serial));

                Device.ServerOptions ??= new ServerOptions();

                deviceConfig.ApplyConfig(Device.ServerOptions);

                if (displays.Count == 0 || _appName != null || Device.ServerOptions.DisplayId == -1)
                {
                    if (string.IsNullOrEmpty(Device.ServerOptions.NewDisplay))
                    {
                        Device.ServerOptions.NewDisplay = " ";
                    }
                }
                else
                {
                    Device.ServerOptions.NewDisplay = null;
                    var displayId = displays.Keys.ToArray()[0];
                    Device.ServerOptions.DisplayId = displayId;
                    _screenSize = new Size(displays[displayId].height, displays[displayId].width);
                    InputProcessor.UpdateScreenSize(_screenSize);
                }

                // 检查音频设备是否可用
                bool isAudioAvailable = _audioPlayer.IsAudioDeviceAvailable;

                // 初始化音频流
                if (isAudioAvailable && Device.ServerOptions.Audio)
                {
                    if (!_audioPlayer.IsActivate())
                    {
                        _audioPlayer.ConfigureAudioDevice();
                    }
                    _audioStreamId = _audioPlayer.StreamPlayStart(
                        AudioDecoder.TARGET_SAMPLE_RATE,
                        AudioDecoder.TARGET_CHANNELS);
                }

                var ports = await tool.DeployServerAsync(Device, isAudioAvailable);
                await Task.Delay(2000);
                bool connected = _scrcpyClient.Connect(ports);

                if (!connected)
                {
                    Device.ServerOptions = null;
                    throw new Exception("Failed to connect to scrcpy server.");
                }

                Device.ServerOptions = null;

                if (_appName != null)
                {
                    var keyMsg = new ControlMsg
                    {
                        Type = ControlMsgType.StartApp,
                        Data = _appName
                    };
                    _scrcpyClient.SendControlCommand(keyMsg.Serialize());
                }
            });

            IsConnected = true;

            SetupEventHandlers();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScreenTab] ConnectDevice failed: {ex}");

            // 连接失败时清理已分配的资源
            if (_audioStreamId >= 0)
            {
                _audioPlayer.StopStream(_audioStreamId);
                _audioStreamId = -1;
            }

            _scrcpyClient?.DisConnect();
            _scrcpyClient?.Dispose();
            _scrcpyClient = null;

            var localizer = Services.Localization.LocalizationManager.Instance;
            DialogHelper.ShowToast(localizer.GetString("ScreenTab.ConnectFailed", "连接失败"), ex.Message, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// 切换侧边栏可见性
    /// </summary>
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    #region 控制按钮命令

    [RelayCommand]
    private void SendKeyAction(string buttonName)
    {
        int keycode = GetButtonKeycode(buttonName);
        if (keycode == 0)
        {
            HandleSpecialButton(buttonName);
            return;
        }

        // 发送按下+释放
        SendKeyDown(keycode);
        SendKeyUp(keycode);
    }

    private void HandleSpecialButton(string buttonName)
    {
        switch (buttonName)
        {
            case "ScreenOn":
                _scrcpyClient?.SendControlCommand(new ControlMsg
                {
                    Type = ControlMsgType.SetScreenPowerMode,
                    Data = true
                }.Serialize());
                break;
            case "ScreenOff":
                _scrcpyClient?.SendControlCommand(new ControlMsg
                {
                    Type = ControlMsgType.SetScreenPowerMode,
                    Data = false
                }.Serialize());
                break;
        }
    }

    private void SendKeyDown(int keycode)
    {
        var keyMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectKeycode,
            Data = new InjectKeycodeData
            {
                Action = AndroidKeyEventAction.Down,
                Keycode = keycode,
                Repeat = 0,
                MetaState = 0
            }
        };
        _scrcpyClient?.SendControlCommand(keyMsg.Serialize());
    }

    private void SendKeyUp(int keycode)
    {
        var keyMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectKeycode,
            Data = new InjectKeycodeData
            {
                Action = AndroidKeyEventAction.Up,
                Keycode = keycode,
                Repeat = 0,
                MetaState = 0
            }
        };
        _scrcpyClient?.SendControlCommand(keyMsg.Serialize());
    }

    private static int GetButtonKeycode(string buttonName)
    {
        return buttonName switch
        {
            "Power" => KeyCode.KEYCODE_POWER,
            "VolumeUp" => KeyCode.KEYCODE_VOLUME_UP,
            "VolumeDown" => KeyCode.KEYCODE_VOLUME_DOWN,
            "Mute" => KeyCode.KEYCODE_164,
            "Back" => KeyCode.KEYCODE_BACK,
            "Home" => KeyCode.KEYCODE_HOME,
            "Menu" => KeyCode.KEYCODE_82,
            _ => 0,
        };
    }

    #endregion

    #region 触控/键盘事件处理

    private void SetupEventHandlers()
    {
        if (_videoImage == null) return;

        // 先移除已有的事件绑定 防止重复注册
        DetachEventHandlers();

        _videoImage.PointerMoved += VideoImage_PointerMoved;
        _videoImage.PointerCaptureLost += VideoImage_PointerCaptureLost;

        _videoImage.AddHandler(
            InputElement.PointerPressedEvent,
            VideoImage_PointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);

        _videoImage.AddHandler(
            InputElement.PointerReleasedEvent,
            VideoImage_PointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);

        _videoImage.SizeChanged += VideoImage_SizeChanged;
        _videoImage.PointerWheelChanged += VideoImage_PointerWheelChanged;

        _videoImage.AddHandler(
            InputElement.KeyDownEvent,
            VideoImage_KeyDown,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);

        _videoImage.AddHandler(
            InputElement.KeyUpEvent,
            VideoImage_KeyUp,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);

        _videoImage.Focusable = true;
    }

    private void DetachEventHandlers()
    {
        if (_videoImage == null) return;

        _videoImage.PointerMoved -= VideoImage_PointerMoved;
        _videoImage.PointerCaptureLost -= VideoImage_PointerCaptureLost;
        _videoImage.SizeChanged -= VideoImage_SizeChanged;
        _videoImage.PointerWheelChanged -= VideoImage_PointerWheelChanged;

        _videoImage.RemoveHandler(InputElement.PointerPressedEvent, VideoImage_PointerPressed);
        _videoImage.RemoveHandler(InputElement.PointerReleasedEvent, VideoImage_PointerReleased);
        _videoImage.RemoveHandler(InputElement.KeyDownEvent, VideoImage_KeyDown);
        _videoImage.RemoveHandler(InputElement.KeyUpEvent, VideoImage_KeyUp);

        InputProcessor.ClearAll(_scrcpyClient);
        _isPointerCaptured = false;
    }

    private void VideoImage_KeyDown(object? sender, KeyEventArgs e)
    {
        InputProcessor.ProcessKeyDown(e, _scrcpyClient);
    }

    private void VideoImage_KeyUp(object? sender, KeyEventArgs e)
    {
        InputProcessor.ProcessKeyUp(e, _scrcpyClient);
    }

    private void VideoImage_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        InputProcessor.ClearAll(_scrcpyClient);
        _isPointerCaptured = false;
        _screenSize = new Size(e.NewSize.Width, e.NewSize.Height);
        InputProcessor.UpdateScreenSize(_screenSize);
    }

    private void VideoImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Device?.DeviceData == null || _videoImage == null) return;

        var viewPoint = e.GetPosition(_videoImage);
        var point = NormalizeCoordinates(viewPoint);

        InputProcessor.ProcessPointerPressed(e, point, _scrcpyClient);

        e.Pointer.Capture(_videoImage);
        _isPointerCaptured = true;
    }

    private void VideoImage_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_isPointerCaptured || Device?.DeviceData == null || _videoImage == null) return;

        var viewPoint = e.GetPosition(_videoImage);
        var point = NormalizeCoordinates(viewPoint);

        InputProcessor.ProcessPointerWheelChanged(e, point, _scrcpyClient);
    }

    private void VideoImage_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerCaptured || Device?.DeviceData == null || _videoImage == null) return;

        var viewPoint = e.GetPosition(_videoImage);
        var point = NormalizeCoordinates(viewPoint);

        InputProcessor.ProcessPointerMoved(e, point, _scrcpyClient);
    }

    private void VideoImage_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerCaptured || Device?.DeviceData == null || _videoImage == null) return;

        var viewPoint = e.GetPosition(_videoImage);
        var point = NormalizeCoordinates(viewPoint);

        InputProcessor.ProcessPointerReleased(e, point, _scrcpyClient);
        
        e.Pointer.Capture(null);
        _isPointerCaptured = false;
    }

    private void VideoImage_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        InputProcessor.ProcessPointerCaptureLost(e, _scrcpyClient);
        e.Pointer.Capture(null);
        _isPointerCaptured = false;
    }

    private Point NormalizeCoordinates(Point viewPoint)
    {
        if (_videoImage == null || _videoImage.Bounds.Width <= 0 || _videoImage.Bounds.Height <= 0)
            return new Point(0, 0);

        double scaleX = _screenSize.Width / _videoImage.Bounds.Width;
        double scaleY = _screenSize.Height / _videoImage.Bounds.Height;

        return new Point(
            Math.Clamp(viewPoint.X * scaleX, 0, _screenSize.Width),
            Math.Clamp(viewPoint.Y * scaleY, 0, _screenSize.Height));
    }

    #endregion
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 释放托管资源
                _disposed = true;
                DetachEventHandlers();
                DetachVideoImage();
                InputProcessor?.Dispose();

                // 停止音频流
                if (_audioStreamId >= 0)
                {
                    _audioPlayer.StopStream(_audioStreamId);
                    _audioStreamId = -1;
                }

                _scrcpyClient?.DisConnect();
                _scrcpyClient?.Dispose();

                VideoSource?.Dispose();
                VideoSource = null;
            }
            _disposed = true;
        }
    }
}
