using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Core.Scrcpy.Control;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Input;
using AYLink.Desktop.Services.ScreenSessions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static AYLink.Core.Scrcpy.Control.ControlMsgModel;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 投屏标签页 ViewModel - 每个设备对应一个标签页
/// 管理投屏会话、视频渲染和控制输入
/// </summary>
public partial class ScreenTabViewModel : TabItemViewModelBase, IDisposable, IAsyncTabStopAware
{
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsToolbarVisible { get; set; } = true;

    [ObservableProperty]
    public partial Avalonia.Media.Imaging.WriteableBitmap? VideoSource { get; set; }

    [ObservableProperty]
    private bool _isMouseLocked;

    public IInputProcessor InputProcessor { get; private set; } = new DefaultInputProcessor();

    private Size _screenSize;
    private bool _disposed;
    private Size _containerSize;
    private bool _hasReceivedFirstVideoFrame;
    private int _renderedFrameCount;
    private Size? _pendingResizeSize;
    private Size? _lastResizeRequestSize;
    private readonly DispatcherTimer _resizeThrottleTimer;
    private readonly IMouseLockService _mouseLockService = new SdlMouseLockService();
    private readonly ScreenInputCoordinator _inputCoordinator;
    private readonly IScreenSession _screenSession;
    private readonly IResizableScreenSession? _resizableScreenSession;
    private static readonly TimeSpan ResizeThrottleInterval = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// 视频 Image 控件引用
    /// </summary>
    private Image? _videoImage;

    private Func<bool> _keyboardInputGate = static () => true;

    private readonly IScreenSessionFactory _screenSessionFactory;

    public ScreenTabViewModel(IScreenSession screenSession, IScreenSessionFactory screenSessionFactory, DeviceModel? device, string title)
    {
        Device = device;
        _screenSession = screenSession ?? throw new ArgumentNullException(nameof(screenSession));
        _screenSessionFactory = screenSessionFactory ?? throw new ArgumentNullException(nameof(screenSessionFactory));
        _resizableScreenSession = _screenSession as IResizableScreenSession;
        _resizeThrottleTimer = CreateResizeTimer();
        _inputCoordinator = CreateInputCoordinator();

        BindScreenSession();
        Title = title;
    }

    public void SetKeyboardInputGate(Func<bool> keyboardInputGate)
    {
        _keyboardInputGate = keyboardInputGate ?? throw new ArgumentNullException(nameof(keyboardInputGate));
    }

    /// <summary>
    /// 绑定视频显示控件
    /// 首次绑定时启动连接；重排/重新挂载时恢复引用并重新绑定输入事件
    /// </summary>
    public void AttachVideoImage(Image videoImage)
    {
        if (ReferenceEquals(_videoImage, videoImage)) return;

        DetachEventHandlers();

        _videoImage = videoImage;
        _mouseLockService.Attach(videoImage);

        if (!IsConnected)
        {
            _ = ConnectDeviceAsync();
        }
        else
        {
            SetupEventHandlers();
            Dispatcher.UIThread.Post(() => _videoImage?.InvalidateVisual());
        }
    }

    /// <summary>
    /// 分离视频显示控件引用
    /// 仅解绑事件并置空引用
    /// 避免重排标签页时导致后端中断
    /// </summary>
    public void DetachVideoImage()
    {
        DetachEventHandlers();
        _mouseLockService.Detach();
        _videoImage = null;
    }

    /// <summary>
    /// 关闭标签页 - 先释放资源再触发关闭事件
    /// </summary>
    protected override void CloseTab()
    {
        base.CloseTab();
    }

    /// <summary>
    /// 连接设备并开始投屏
    /// </summary>
    private async Task ConnectDeviceAsync()
    {
        if (_videoImage == null || _disposed)
        {
            return;
        }

        try
        {
            RecreateInputProcessor();
            InputProcessor.CursorLockRequested += OnCursorLockRequested;
            InputProcessor.SetCursorLocked(IsMouseLocked);
            await _screenSession.StartAsync(InputProcessor);
            SetupEventHandlers();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScreenTab] ConnectDevice failed: {ex}");

            var localizer = Services.Localization.LocalizationManager.Instance;
            Services.Notifications.NotificationService.Instance.ShowError(localizer.GetString("ScreenTab.ConnectFailed", "连接失败"), ex.Message);
        }
    }

    public bool IsFlexDisplayEnabled => _resizableScreenSession?.IsFlexDisplayEnabled == true;

    private void OnVideoFrameDecoded(int width, int height, IntPtr bgraDataPtr, int rowBytes)
    {
        if (_disposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            try
            {
                if (!_hasReceivedFirstVideoFrame)
                {
                    _hasReceivedFirstVideoFrame = true;
                    Debug.WriteLine($"[ScreenTab] first video frame rendered candidate: size={width}x{height}");
                    FlushPendingResize();
                }

                if (VideoSource == null || VideoSource.PixelSize.Width != width || VideoSource.PixelSize.Height != height)
                {
                    var oldBitmap = VideoSource;
                    VideoSource = new Avalonia.Media.Imaging.WriteableBitmap(
                        new PixelSize(width, height),
                        new Vector(96, 96),
                        Avalonia.Platform.PixelFormat.Bgra8888,
                        Avalonia.Platform.AlphaFormat.Premul);
                    oldBitmap?.Dispose();

                    _screenSize = new Size(width, height);
                    InputProcessor.UpdateScreenSize(_screenSize);
                }

                using var buf = VideoSource.Lock();
                unsafe
                {
                    Buffer.MemoryCopy(
                        bgraDataPtr.ToPointer(),
                        buf.Address.ToPointer(),
                        buf.RowBytes * height,
                        rowBytes * height);
                }

                _renderedFrameCount++;
                if (_renderedFrameCount <= 3 || _renderedFrameCount % 120 == 0)
                {
                    Debug.WriteLine($"[ScreenTab] frame copied to bitmap: count={_renderedFrameCount}, size={width}x{height}, rowBytes={rowBytes}");
                }

                _videoImage?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenTab] Render frame error: {ex.Message}");
            }
        }, DispatcherPriority.Render);
    }

    private void OnCursorLockRequested(bool locked)
    {
        ApplyMouseLockState(locked);
        ShowMouseLockToast(locked);
    }

    [RelayCommand]
    private void ToggleMouseLock()
    {
        var locked = !IsMouseLocked;
        ApplyMouseLockState(locked);
        ShowMouseLockToast(locked);
    }

    private void ApplyMouseLockState(bool locked)
        => ApplyMouseLockState(locked, false);

    private void ApplyMouseLockState(bool locked, bool showToast)
    {
        InputProcessor.SetCursorLocked(locked);
        _mouseLockService.SetLocked(locked);
        _inputCoordinator.SetMouseLocked(locked);
        IsMouseLocked = locked;

        if (showToast)
        {
            ShowMouseLockToast(locked);
        }
    }

    private static void ShowMouseLockToast(bool locked)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        if (locked)
        {
            Services.Notifications.ToastManager.Instance.Show(
                localizer.GetString("ScreenTab.CursorLockedTitle", "光标已锁定"),
                localizer.GetString("ScreenTab.CursorLockedMessage", "按 Alt 键解锁光标")
            );
        }
        else
        {
            Services.Notifications.ToastManager.Instance.Show(
                localizer.GetString("ScreenTab.CursorUnlockedTitle", "光标已解锁"),
                localizer.GetString("ScreenTab.CursorUnlockedMessage", "光标已恢复自由移动")
            );
        }
    }

    /// <summary>
    /// 切换侧边栏可见性
    /// </summary>
    [RelayCommand]
    private void ToggleToolbar()
    {
        IsToolbarVisible = !IsToolbarVisible;
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

        SendKeyDown(keycode);
        SendKeyUp(keycode);
    }

    private void HandleSpecialButton(string buttonName)
    {
        switch (buttonName)
        {
            case "ScreenOn":
                SendControlCommand(new ControlMsg
                {
                    Type = ControlMsgType.SetScreenPowerMode,
                    Data = true
                }.Serialize());
                break;
            case "ScreenOff":
                SendControlCommand(new ControlMsg
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
        SendControlCommand(keyMsg.Serialize());
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
        SendControlCommand(keyMsg.Serialize());
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
        if (_videoImage != null)
        {
            _inputCoordinator.Attach(_videoImage);
        }
    }

    private void DetachEventHandlers()
    {
        _inputCoordinator.Detach();
    }

    public void UpdateContainerSize(Size newSize)
    {
        _containerSize = newSize;
        _pendingResizeSize = newSize;
        _resizeThrottleTimer.Stop();
        _resizeThrottleTimer.Start();
    }

    private void OnResizeThrottleTimerTick(object? sender, EventArgs e)
    {
        _resizeThrottleTimer.Stop();
        FlushPendingResize();
    }

    private void FlushPendingResize()
    {
        if (_pendingResizeSize is not Size pendingSize)
        {
            return;
        }

        SendResizeDisplayIfNeeded(pendingSize);
    }

    private void SendResizeDisplayIfNeeded(Size newSize)
    {
        if (_resizableScreenSession != null &&
            _resizableScreenSession.SendResizeDisplayIfNeeded(newSize, _hasReceivedFirstVideoFrame, _lastResizeRequestSize))
        {
            _lastResizeRequestSize = newSize;
        }
    }

    private bool CanHandleKeyboardInput()
        => _keyboardInputGate();

    /// <summary>
    /// 在标签页销毁前停止当前投屏会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _screenSession.StopAsync(cancellationToken);
        IsConnected = false;
    }

    private Point NormalizeCoordinates(Point viewPoint)
    {
        if (_videoImage == null || _videoImage.Bounds.Width <= 0 || _videoImage.Bounds.Height <= 0 || _screenSize.Width <= 0 || _screenSize.Height <= 0)
            return new Point(0, 0);

        double controlWidth = _videoImage.Bounds.Width;
        double controlHeight = _videoImage.Bounds.Height;

        double scaleX = controlWidth / _screenSize.Width;
        double scaleY = controlHeight / _screenSize.Height;

        double scale = _videoImage.Stretch == Stretch.Fill ? 1.0 : Math.Min(scaleX, scaleY);

        if (_videoImage.Stretch == Stretch.Fill)
        {
            return new Point(
                Math.Clamp(viewPoint.X / scaleX, 0, _screenSize.Width),
                Math.Clamp(viewPoint.Y / scaleY, 0, _screenSize.Height)
            );
        }

        double drawnWidth = _screenSize.Width * scale;
        double drawnHeight = _screenSize.Height * scale;

        double offsetX = (controlWidth - drawnWidth) / 2;
        double offsetY = (controlHeight - drawnHeight) / 2;

        double imageX = viewPoint.X - offsetX;
        double imageY = viewPoint.Y - offsetY;

        return new Point(
            Math.Clamp(imageX / scale, 0, _screenSize.Width),
            Math.Clamp(imageY / scale, 0, _screenSize.Height));
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
                _disposed = true;
                DetachEventHandlers();
                DetachVideoImage();
                _screenSession.VideoFrameDecoded -= OnVideoFrameDecoded;
                _screenSession.SessionError -= OnSessionError;
                _screenSession.StateChanged -= OnSessionStateChanged;

                if (InputProcessor != null)
                {
                    InputProcessor.CursorLockRequested -= OnCursorLockRequested;
                    InputProcessor.Dispose();
                }

                _inputCoordinator.Dispose();
                _mouseLockService.Dispose();
                _screenSession.Dispose();
                _resizeThrottleTimer.Stop();

                VideoSource?.Dispose();
                VideoSource = null;
            }

            _disposed = true;
        }
    }

    private void SendControlCommand(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return;
        }

        _screenSession.SendControl(payload);
    }

    private DispatcherTimer CreateResizeTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = ResizeThrottleInterval
        };
        timer.Tick += OnResizeThrottleTimerTick;
        return timer;
    }

    private ScreenInputCoordinator CreateInputCoordinator()
    {
        return new ScreenInputCoordinator(
            () => InputProcessor,
            _mouseLockService,
            () => IsMouseLocked,
            CanHandleKeyboardInput,
            () => IsConnected,
            NormalizeCoordinates,
            ApplyMouseLockState);
    }

    private void BindScreenSession()
    {
        _screenSession.VideoFrameDecoded += OnVideoFrameDecoded;
        _screenSession.SessionError += OnSessionError;
        _screenSession.StateChanged += OnSessionStateChanged;
    }

    private void RecreateInputProcessor()
    {
        if (InputProcessor != null)
        {
            InputProcessor.CursorLockRequested -= OnCursorLockRequested;
            InputProcessor.Dispose();
        }

        InputProcessor = _screenSessionFactory.CreateInputProcessor(Device);
    }

    private void OnSessionError(Exception ex)
    {
        Debug.WriteLine($"[ScreenTab] session error: {ex}");
    }

    private void OnSessionStateChanged(ScreenSessionState state)
    {
        IsConnected = state == ScreenSessionState.Connected;
        Debug.WriteLine($"[ScreenTab] session state: {state}");
    }
}
