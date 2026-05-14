using AYLink.Core.Models;
using AYLink.Core.Scrcpy.Control;
using AYLink.Core.Scrcpy;
using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Services.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsToolbarVisible { get; set; } = true;

    [ObservableProperty]
    public partial Avalonia.Media.Imaging.WriteableBitmap? VideoSource { get; set; }
    
    [ObservableProperty]
    private bool _isMouseLocked;
    
    public IInputProcessor InputProcessor { get; private set; } = new DefaultInputProcessor();

    private Size _screenSize;
    private readonly string? _appPackageName;
    private readonly string? _appDisplayName;
    private bool _disposed;
    private readonly AudioPlayer _audioPlayer = AudioPlayer.Instance;
    private Size _containerSize;
    private bool _hasReceivedFirstVideoFrame;
    private Size? _pendingResizeSize;
    private Size? _lastResizeRequestSize;
    private readonly DispatcherTimer _resizeThrottleTimer;
    private readonly IMouseLockService _mouseLockService = new SdlMouseLockService();
    private readonly ScreenInputCoordinator _inputCoordinator;
    private readonly ScreenSessionController _sessionController;
    private static readonly TimeSpan ResizeThrottleInterval = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// 视频 Image 控件引用
    /// </summary>
    private Image? _videoImage;
    private Func<bool> _keyboardInputGate = static () => true;

    public ScreenTabViewModel(DeviceModel device, string? appPackageName = null, string? appDisplayName = null)
    {
        Device = device;
        _appPackageName = appPackageName;
        _appDisplayName = appDisplayName;
        _resizeThrottleTimer = new DispatcherTimer
        {
            Interval = ResizeThrottleInterval
        };
        _resizeThrottleTimer.Tick += OnResizeThrottleTimerTick;
        _sessionController = new ScreenSessionController(device, appPackageName, _audioPlayer);
        _inputCoordinator = new ScreenInputCoordinator(
            () => InputProcessor,
            _mouseLockService,
            () => IsMouseLocked,
            CanHandleKeyboardInput,
            () => Device?.DeviceData != null,
            NormalizeCoordinates,
            ApplyMouseLockState);
        _sessionController.VideoFrameDecoded += OnVideoFrameDecoded;

        var titleAppName = string.IsNullOrWhiteSpace(appDisplayName) ? appPackageName : appDisplayName;
        Title = titleAppName != null ? $"{device.Name} - {titleAppName}" : device.Name;
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
        // 如果是同一个实例 无需处理
        if (ReferenceEquals(_videoImage, videoImage)) return;

        // 先解绑旧控件的事件（如果有旧引用）
        DetachEventHandlers();

        _videoImage = videoImage;
        _mouseLockService.Attach(videoImage);

        if (Device != null && !IsConnected)
        {
            // 首次连接
            _ = ConnectDeviceAsync();
        }
        else if (IsConnected)
        {
            // 已连接状态下重新挂载（重排/视图切换）- 重新绑定输入事件
            SetupEventHandlers();
            
            // 强行刷新一次画面 防止视图复用时卡在上一帧旧图
            Dispatcher.UIThread.Post(() =>
            {
                _videoImage?.InvalidateVisual();
            });
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
        _mouseLockService.Detach();
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
            var deviceConfig = ConfigManager.Instance.LoadConfig<DeviceConfig>(
                HashHelper.ToMd5Hash(Device.Serial));
            Device.ServerOptions ??= new ServerOptions();
            deviceConfig.ApplyConfig(Device.ServerOptions);

            // 根据配置选择输入处理器
            if (InputProcessor != null)
            {
                InputProcessor.CursorLockRequested -= OnCursorLockRequested;
                InputProcessor.Dispose();
            }

            if (Device.ServerOptions.HidKeyboard || Device.ServerOptions.HidMouse)
            {
                InputProcessor = new HidInputProcessor(Device.ServerOptions.HidKeyboard, Device.ServerOptions.HidMouse);
            }
            else
            {
                InputProcessor = new DefaultInputProcessor();
            }

            InputProcessor.CursorLockRequested += OnCursorLockRequested;
            InputProcessor.SetCursorLocked(IsMouseLocked);
            await _sessionController.ConnectAsync(InputProcessor);

            IsConnected = true;

            SetupEventHandlers();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScreenTab] ConnectDevice failed: {ex}");

            var localizer = Services.Localization.LocalizationManager.Instance;
            Services.Notifications.NotificationService.Instance.ShowError(localizer.GetString("ScreenTab.ConnectFailed", "连接失败"), ex.Message);
        }
    }

    public bool IsFlexDisplayEnabled => _sessionController.IsFlexDisplayEnabled;

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

        // 发送按下+释放
        SendKeyDown(keycode);
        SendKeyUp(keycode);
    }

    private void HandleSpecialButton(string buttonName)
    {
        switch (buttonName)
        {
            case "ScreenOn":
                _sessionController.Client?.SendControlCommand(new ControlMsg
                {
                    Type = ControlMsgType.SetScreenPowerMode,
                    Data = true
                }.Serialize());
                break;
            case "ScreenOff":
                _sessionController.Client?.SendControlCommand(new ControlMsg
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
        _sessionController.Client?.SendControlCommand(keyMsg.Serialize());
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
        _sessionController.Client?.SendControlCommand(keyMsg.Serialize());
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
        if (_sessionController.SendResizeDisplayIfNeeded(newSize, _hasReceivedFirstVideoFrame, _lastResizeRequestSize))
        {
            _lastResizeRequestSize = newSize;
        }
    }

    private bool CanHandleKeyboardInput()
        => _keyboardInputGate();

    private Point NormalizeCoordinates(Point viewPoint)
    {
        if (_videoImage == null || _videoImage.Bounds.Width <= 0 || _videoImage.Bounds.Height <= 0 || _screenSize.Width <= 0 || _screenSize.Height <= 0)
            return new Point(0, 0);

        double controlWidth = _videoImage.Bounds.Width;
        double controlHeight = _videoImage.Bounds.Height;

        // 根据实际使用的 Stretch 模式计算实际渲染图片的缩放比例
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

        // 计算渲染出来的实际图片大小
        double drawnWidth = _screenSize.Width * scale;
        double drawnHeight = _screenSize.Height * scale;

        // 图片默认在中心，计算相对控件边缘的留白偏移量
        double offsetX = (controlWidth - drawnWidth) / 2;
        double offsetY = (controlHeight - drawnHeight) / 2;

        // 将鼠标相对控件坐标 转换成 相对图片坐标
        double imageX = viewPoint.X - offsetX;
        double imageY = viewPoint.Y - offsetY;

        // 还原回原始视频的坐标系 (去除缩放比例)
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
                // 释放托管资源
                _disposed = true;
                DetachEventHandlers();
                DetachVideoImage();
                if (InputProcessor != null)
                {
                    InputProcessor.CursorLockRequested -= OnCursorLockRequested;
                    InputProcessor.Dispose();
                }
                _inputCoordinator.Dispose();
                _mouseLockService.Dispose();
                _sessionController.Dispose();
                _resizeThrottleTimer.Stop();

                VideoSource?.Dispose();
                VideoSource = null;
            }
            _disposed = true;
        }
    }
}
