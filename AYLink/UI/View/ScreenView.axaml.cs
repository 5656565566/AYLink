using AYLink.Scrcpy;
using AYLink.Scrcpy.Control;
using AYLink.UI.Themes;
using AYLink.UIModel;
using AYLink.Utils;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AYLink.Scrcpy.Control.ControlMsgModel;

namespace AYLink.UI;

public partial class ScreenView : UserControl
{
    readonly BackgroundImageManager backgroundImageManager = BackgroundImageManager.Instance;

    private ScrcpyClient? _scrcpyClient;
    private DeviceModel? _deviceModel;
    private Size _screenSize;
    private readonly Dictionary<int, ulong> _pointerIdMap = [];
    private ulong _nextPointerId;
    private readonly string? _appName;
    private bool _isPointerCaptured;
    private readonly ConfigManager configManager = ConfigManager.Instance;

    public ScreenView() // 为了ui预览可以正常显示
    {
        InitializeComponent();
    }

    public ScreenView(DeviceModel deviceModel, string? appName)
    {
        InitializeComponent();

        backgroundImageManager.RegisterImageComponent(BackgroundImage);

        _appName = appName;
        _scrcpyClient = new ScrcpyClient(VideoImage, deviceModel);
        _deviceModel = deviceModel;

        Task.Run(() =>
        {
            ConnectDevice(null);
        });

        ExpandBtn.Click += ExpandBtn_Click;
        CollapseBtn.Click += CollapseBtn_Click;

        SetupEventHandlers();
    }

    public void Dispose()
    {
        backgroundImageManager.UnregisterImageComponent(BackgroundImage);

        _scrcpyClient?.DisConnect();
        _scrcpyClient?.Dispose();
    }

    public async void ConnectDevice(int? displayNum)
    {
        if (_deviceModel!.AdbClient == null) return;

        var server = new ScrcpyTool(_deviceModel);
        var displays = server.GetResolutions();

        var deviceConfig = configManager.LoadConfig<DeviceConfig>(HashHelper.ToMd5Hash(_deviceModel.Serial));

        if (_deviceModel.ServerOptions == null)
        {
            _deviceModel.ServerOptions = new ServerOptions()
            {
                DisplayId = displayNum,
            };
        }

        deviceConfig.ApplyConfig(_deviceModel.ServerOptions);

        if (displays.Count == 0 || _appName != null || _deviceModel.ServerOptions.DisplayId == -1)
        {
            if (string.IsNullOrEmpty(_deviceModel.ServerOptions.NewDisplay))
            {
                _deviceModel.ServerOptions.NewDisplay = " "; // 默认跟随主屏幕
            }
        }
        else
        {
            _deviceModel.ServerOptions.NewDisplay = null;
            var displayId = displays.Keys.ToArray()[displayNum ?? 0];
            _deviceModel.ServerOptions.DisplayId = displayId;
            _screenSize = new Size(displays[displayId].height, displays[displayId].width);
        }

        var ports = await server.DeployServerAsync();

        await Task.Delay(2000);
        _scrcpyClient!.Connect(ports);

        _deviceModel.ServerOptions = null;

        if (_appName != null)
        {
            var keyMsg = new ControlMsg
            {
                Type = ControlMsgType.StartApp,
                Data = _appName
            };
            _scrcpyClient?.SendControlCommand(keyMsg.Serialize());
        }
    }

    private void CollapseBtn_Click(object? sender, RoutedEventArgs e)
    {
        Sidebar.IsVisible = false;
        ExpandBtn.IsVisible = true;
    }


    private void ExpandBtn_Click(object? sender, RoutedEventArgs e)
    {
        Sidebar.IsVisible = true;
        ExpandBtn.IsVisible = false;
    }
    private void SetupEventHandlers()
    {
        VideoImage.PointerMoved += VideoImage_PointerMoved;
        VideoImage.PointerCaptureLost += VideoImage_PointerCaptureLost;

        VideoImage.AddHandler(
            PointerPressedEvent,
            VideoImage_PointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true
        );

        VideoImage.AddHandler(
            PointerReleasedEvent,
            VideoImage_PointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true
        );

        VideoImage.SizeChanged += VideoImage_SizeChanged;
        VideoImage.PointerWheelChanged += VideoImage_PointerWheelChanged;

        VideoImage.AddHandler(
            KeyDownEvent,
            VideoImage_KeyDown,
            RoutingStrategies.Bubble,
            handledEventsToo: true
        );

        VideoImage.AddHandler(
            KeyUpEvent,
            VideoImage_KeyUp,
            RoutingStrategies.Bubble,
            handledEventsToo: true
        );

        VideoImage.Focusable = true;

        SetupButtonEvents();
    }

    private static int GetKeyId(string name)
    {
        switch (name.ToUpper())
        {
            case "A":
                return KeyCode.KEYCODE_A;
            case "B":
                return KeyCode.KEYCODE_B;
            case "C":
                return KeyCode.KEYCODE_C;
            case "D":
                return KeyCode.KEYCODE_D;
            case "E":
                return KeyCode.KEYCODE_E;
            case "F":
                return KeyCode.KEYCODE_F;
            case "G":
                return KeyCode.KEYCODE_G;
            case "H":
                return KeyCode.KEYCODE_H;
            case "I":
                return KeyCode.KEYCODE_I;
            case "J":
                return KeyCode.KEYCODE_J;
            case "K":
                return KeyCode.KEYCODE_K;
            case "L":
                return KeyCode.KEYCODE_L;
            case "M":
                return KeyCode.KEYCODE_M;
            case "N":
                return KeyCode.KEYCODE_N;
            case "O":
                return KeyCode.KEYCODE_O;
            case "P":
                return KeyCode.KEYCODE_P;
            case "Q":
                return KeyCode.KEYCODE_Q;
            case "R":
                return KeyCode.KEYCODE_R;
            case "S":
                return KeyCode.KEYCODE_S;
            case "T":
                return KeyCode.KEYCODE_T;
            case "U":
                return KeyCode.KEYCODE_U;
            case "V":
                return KeyCode.KEYCODE_V;
            case "W":
                return KeyCode.KEYCODE_W;
            case "X":
                return KeyCode.KEYCODE_X;
            case "Y":
                return KeyCode.KEYCODE_Y;
            case "Z":
                return KeyCode.KEYCODE_Z;
            case "0":
                return KeyCode.KEYCODE_0;
            case "1":
                return KeyCode.KEYCODE_1;
            case "2":
                return KeyCode.KEYCODE_2;
            case "3":
                return KeyCode.KEYCODE_3;
            case "4":
                return KeyCode.KEYCODE_4;
            case "5":
                return KeyCode.KEYCODE_5;
            case "6":
                return KeyCode.KEYCODE_6;
            case "7":
                return KeyCode.KEYCODE_7;
            case "8":
                return KeyCode.KEYCODE_8;
            case "9":
                return KeyCode.KEYCODE_9;
            case "ENTER":
                return KeyCode.KEYCODE_66;
            case "ESCAPE":
                return KeyCode.KEYCODE_111;
            case "SPACE":
                return KeyCode.KEYCODE_62;
            case "SHIFT":
                return KeyCode.KEYCODE_59;
            case "CTRL":
                return KeyCode.KEYCODE_113;
            case "ALT":
                return KeyCode.KEYCODE_57;
            case "BACKSPACE":
                return KeyCode.KEYCODE_67;
            default:
                return 0;
        }
    }

    private void VideoImage_KeyDown(object? sender, KeyEventArgs e)
    {
        string keyName = e.Key.ToString();
        var keyId = GetKeyId(keyName);
        if (keyId == 0) return;

        var keyMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectKeycode,
            Data = new InjectKeycodeData
            {
                Action = AndroidKeyEventAction.Down,
                Keycode = keyId,
                Repeat = 0,
                MetaState = 0
            }
        };
        _scrcpyClient?.SendControlCommand(keyMsg.Serialize());
    }

    private void VideoImage_KeyUp(object? sender, KeyEventArgs e)
    {
        string keyName = e.Key.ToString();
        var keyId = GetKeyId(keyName);
        if (keyId == 0) return;

        var keyMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectKeycode,
            Data = new InjectKeycodeData
            {
                Action = AndroidKeyEventAction.Up,
                Keycode = keyId,
                Repeat = 0,
                MetaState = 0
            }
        };
        _scrcpyClient?.SendControlCommand(keyMsg.Serialize());
    }

    private void SetupButtonEvents()
    {
        var buttons = new[] { PowerBtn, VolumeUpBtn, VolumeDownBtn, MuteBtn,
                              ScreenOnBtn, ScreenOffBtn, BackBtn, HomeBtn, MenuBtn };

        foreach (var button in buttons)
        {
            button.AddHandler(
                PointerPressedEvent,
                Btn_PointerPressed,
                RoutingStrategies.Bubble,
                handledEventsToo: true
            );

            button.AddHandler(
                PointerReleasedEvent,
                Btn_PointerReleased,
                RoutingStrategies.Bubble,
                handledEventsToo: true
            );
        }
    }

    private void Btn_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var button = sender as Button;
        int keycode = GetButtonId(button!.Name);

        if (keycode == 0)
        {
            return;
        }

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

    private void Btn_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var button = sender as Button;
        int keycode = GetButtonId(button!.Name);

        if (keycode == 0)
        {
            return;
        }

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

    private int GetButtonId(string? buttonId)
    {
        if (buttonId == null)
        {
            return 0;
        }

        switch (buttonId)
        {
            case "PowerBtn":
                return KeyCode.KEYCODE_POWER;
            case "VolumeUpBtn":
                return KeyCode.KEYCODE_VOLUME_UP;
            case "VolumeDownBtn":
                return KeyCode.KEYCODE_VOLUME_DOWN;
            case "MuteBtn":
                return KeyCode.KEYCODE_164;
            case "ScreenOnBtn":
                _scrcpyClient!.SendControlCommand(new ControlMsg
                {
                    Type = ControlMsgType.SetScreenPowerMode,
                    Data = true
                }.Serialize());
                return 0;

            case "ScreenOffBtn":
                _scrcpyClient!.SendControlCommand(new ControlMsg
                {
                    Type = ControlMsgType.SetScreenPowerMode,
                    Data = false
                }.Serialize());
                return 0;
            case "BackBtn":
                return KeyCode.KEYCODE_BACK;
            case "HomeBtn":
                return KeyCode.KEYCODE_HOME;
            case "MenuBtn":
                return KeyCode.KEYCODE_82;
            default:
                return 0;
        }
    }
    #region 触控处理逻辑

    private void ClearAllTouchPoints()
    {
        foreach (var pointerId in _pointerIdMap.Values)
        {
            var touchMsg = new ControlMsg
            {
                Type = ControlMsgType.InjectTouchEvent,
                Data = new ControlMsg.InjectTouchData
                {
                    Action = AndroidMotionEventAction.Up,
                    PointerId = pointerId,
                    Position = new ScPosition(0, 0, (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                    Pressure = 0.0f,
                    ActionButton = 0,
                    Buttons = 0
                }
            };

            _scrcpyClient!.SendControlCommand(touchMsg.Serialize());
        }

        _pointerIdMap.Clear();
        _nextPointerId = 0;
        _isPointerCaptured = false;
    }

    private void VideoImage_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ClearAllTouchPoints();
        _screenSize = new Size(e.NewSize.Width, e.NewSize.Height);
    }

    private void VideoImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_deviceModel?.DeviceData == null) return;

        var viewPoint = e.GetPosition(VideoImage);
        var point = NormalizeCoordinates(viewPoint);

        int pointerHash = e.Pointer.GetHashCode();
        if (!_pointerIdMap.ContainsKey(pointerHash))
        {
            _pointerIdMap[pointerHash] = _nextPointerId++;
        }

        ulong pointerId = _pointerIdMap[pointerHash];

        var touchMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectTouchEvent,
            Data = new ControlMsg.InjectTouchData
            {
                Action = AndroidMotionEventAction.Down,
                PointerId = pointerId,
                Position = new ScPosition(
                    (int)point.X,
                    (int)point.Y,
                    (ushort)_screenSize.Width,
                    (ushort)_screenSize.Height),
                Pressure = 1.0f,
                ActionButton = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY,
                Buttons = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY
            }
        };

        _scrcpyClient!.SendControlCommand(touchMsg.Serialize());

        e.Pointer.Capture(VideoImage);
        _isPointerCaptured = true;
    }
    private void VideoImage_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_isPointerCaptured || _deviceModel?.DeviceData == null) return;

        var viewPoint = e.GetPosition(VideoImage);
        var point = NormalizeCoordinates(viewPoint);

        var controlMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectScrollEvent,
            Data = new InjectScrollData
            {
                Position = new ScPosition(
                    (int)point.X,
                    (int)point.Y,
                    (ushort)_screenSize.Width,
                    (ushort)_screenSize.Height),
                VScroll = (float)-e.Delta.Y,
                HScroll = (float)-e.Delta.X,
                Buttons = 0
            }
        };
        _scrcpyClient?.SendControlCommand(controlMsg.Serialize());

        e.Handled = true;
    }

    private void VideoImage_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerCaptured || _deviceModel?.DeviceData == null) return;

        var viewPoint = e.GetPosition(VideoImage);
        var point = NormalizeCoordinates(viewPoint);

        int pointerHash = e.Pointer.GetHashCode();
        if (!_pointerIdMap.TryGetValue(pointerHash, out ulong pointerId)) return;

        var touchMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectTouchEvent,
            Data = new ControlMsg.InjectTouchData
            {
                Action = AndroidMotionEventAction.Move,
                PointerId = pointerId,
                Position = new ScPosition(
                    (int)point.X,
                    (int)point.Y,
                    (ushort)_screenSize.Width,
                    (ushort)_screenSize.Height),
                Pressure = 1.0f,
                ActionButton = 0,
                Buttons = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY
            }
        };

        _scrcpyClient!.SendControlCommand(touchMsg.Serialize());
    }

    private void VideoImage_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerCaptured || _deviceModel?.DeviceData == null) return;

        var viewPoint = e.GetPosition(VideoImage);
        var point = NormalizeCoordinates(viewPoint);

        int pointerHash = e.Pointer.GetHashCode();
        if (!_pointerIdMap.TryGetValue(pointerHash, out ulong pointerId)) return;

        var touchMsg = new ControlMsg
        {
            Type = ControlMsgType.InjectTouchEvent,
            Data = new ControlMsg.InjectTouchData
            {
                Action = AndroidMotionEventAction.Up,
                PointerId = pointerId,
                Position = new ScPosition(
                    (int)point.X,
                    (int)point.Y,
                    (ushort)_screenSize.Width,
                    (ushort)_screenSize.Height),
                Pressure = 0.0f,
                ActionButton = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY,
                Buttons = 0
            }
        };

        _scrcpyClient!.SendControlCommand(touchMsg.Serialize());
        ReleasePointer(e.Pointer, pointerHash);
    }

    private void VideoImage_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        int pointerHash = e.Pointer.GetHashCode();
        if (_pointerIdMap.TryGetValue(pointerHash, out ulong pointerId))
        {
            var touchMsg = new ControlMsg
            {
                Type = ControlMsgType.InjectTouchEvent,
                Data = new ControlMsg.InjectTouchData
                {
                    Action = AndroidMotionEventAction.Up,
                    PointerId = pointerId,
                    Position = new ScPosition(0, 0, (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                    Pressure = 0.0f,
                    ActionButton = 0,
                    Buttons = 0
                }
            };
            _scrcpyClient!.SendControlCommand(touchMsg.Serialize());
        }
        ReleasePointer(e.Pointer, pointerHash);
    }

    private void ReleasePointer(IPointer pointer, int pointerHash)
    {
        pointer.Capture(null);
        _pointerIdMap.Remove(pointerHash);
        _isPointerCaptured = false;
    }

    private Point NormalizeCoordinates(Point viewPoint)
    {
        if (VideoImage.Bounds.Width <= 0 || VideoImage.Bounds.Height <= 0)
            return new Point(0, 0);

        double scaleX = _screenSize.Width / VideoImage.Bounds.Width;
        double scaleY = _screenSize.Height / VideoImage.Bounds.Height;

        return new Point(
            Math.Clamp(viewPoint.X * scaleX, 0, _screenSize.Width),
            Math.Clamp(viewPoint.Y * scaleY, 0, _screenSize.Height));
    }

    #endregion
}