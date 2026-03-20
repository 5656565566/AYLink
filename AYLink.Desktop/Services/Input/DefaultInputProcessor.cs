using Avalonia;
using Avalonia.Input;
using AYLink.Core.Scrcpy;
using AYLink.Core.Scrcpy.Control;
using System.Collections.Generic;

namespace AYLink.Desktop.Services.Input;

/// <summary>
/// 默认的注入模式输入处理器
/// 负责将PC端的键鼠事件直接转换为Android的触摸和按键事件
/// </summary>
public class DefaultInputProcessor : IInputProcessor
{
    private Size _screenSize;
    private readonly Dictionary<int, ulong> _pointerIdMap = [];
    private ulong _nextPointerId;

    public void UpdateScreenSize(Size size)
    {
        _screenSize = size;
    }

    public void ProcessPointerPressed(PointerPressedEventArgs e, Point videoPoint, ScrcpyClient? client)
    {
        if (client == null) return;

        int pointerHash = e.Pointer.GetHashCode();
        if (!_pointerIdMap.TryGetValue(pointerHash, out ulong pointerId))
        {
            pointerId = _nextPointerId++;
            _pointerIdMap[pointerHash] = pointerId;
        }

        var touchMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
            Data = new ControlMsgModel.ControlMsg.InjectTouchData
            {
                Action = ControlMsgModel.AndroidMotionEventAction.Down,
                PointerId = pointerId,
                Position = new ControlMsgModel.ScPosition(
                    (int)videoPoint.X, (int)videoPoint.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                Pressure = 1.0f,
                ActionButton = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY,
                Buttons = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY
            }
        };

        client.SendControlCommand(touchMsg.Serialize());
    }

    public void ProcessPointerMoved(PointerEventArgs e, Point videoPoint, ScrcpyClient? client)
    {
        if (client == null) return;

        int pointerHash = e.Pointer.GetHashCode();
        if (!_pointerIdMap.TryGetValue(pointerHash, out ulong pointerId)) return;

        var touchMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
            Data = new ControlMsgModel.ControlMsg.InjectTouchData
            {
                Action = ControlMsgModel.AndroidMotionEventAction.Move,
                PointerId = pointerId,
                Position = new ControlMsgModel.ScPosition(
                    (int)videoPoint.X, (int)videoPoint.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                Pressure = 1.0f,
                ActionButton = 0,
                Buttons = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY
            }
        };

        client.SendControlCommand(touchMsg.Serialize());
    }

    public void ProcessPointerReleased(PointerReleasedEventArgs e, Point videoPoint, ScrcpyClient? client)
    {
        if (client == null) return;

        int pointerHash = e.Pointer.GetHashCode();
        if (!_pointerIdMap.TryGetValue(pointerHash, out ulong pointerId)) return;

        var touchMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
            Data = new ControlMsgModel.ControlMsg.InjectTouchData
            {
                Action = ControlMsgModel.AndroidMotionEventAction.Up,
                PointerId = pointerId,
                Position = new ControlMsgModel.ScPosition(
                    (int)videoPoint.X, (int)videoPoint.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                Pressure = 0.0f,
                ActionButton = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY,
                Buttons = 0
            }
        };

        client.SendControlCommand(touchMsg.Serialize());
        _pointerIdMap.Remove(pointerHash);
    }

    public void ProcessPointerWheelChanged(PointerWheelEventArgs e, Point videoPoint, ScrcpyClient? client)
    {
        if (client == null) return;

        var controlMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectScrollEvent,
            Data = new ControlMsgModel.InjectScrollData
            {
                Position = new ControlMsgModel.ScPosition(
                    (int)videoPoint.X, (int)videoPoint.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                VScroll = (float)-e.Delta.Y,
                HScroll = (float)-e.Delta.X,
                Buttons = 0
            }
        };
        client.SendControlCommand(controlMsg.Serialize());
        e.Handled = true;
    }

    public void ProcessPointerCaptureLost(PointerCaptureLostEventArgs e, ScrcpyClient? client)
    {
        int pointerHash = e.Pointer.GetHashCode();
        if (_pointerIdMap.TryGetValue(pointerHash, out ulong pointerId))
        {
            if (client != null)
            {
                var touchMsg = new ControlMsgModel.ControlMsg
                {
                    Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
                    Data = new ControlMsgModel.ControlMsg.InjectTouchData
                    {
                        Action = ControlMsgModel.AndroidMotionEventAction.Up,
                        PointerId = pointerId,
                        Position = new ControlMsgModel.ScPosition(0, 0, (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                        Pressure = 0.0f,
                        ActionButton = 0,
                        Buttons = 0
                    }
                };
                client.SendControlCommand(touchMsg.Serialize());
            }
            _pointerIdMap.Remove(pointerHash);
        }
    }

    public void ProcessKeyDown(KeyEventArgs e, ScrcpyClient? client)
    {
        if (client == null) return;
        var keyId = GetKeyId(e.Key.ToString());
        if (keyId == 0) return;
        SendKey(client, ControlMsgModel.AndroidKeyEventAction.Down, keyId);
    }

    public void ProcessKeyUp(KeyEventArgs e, ScrcpyClient? client)
    {
        if (client == null) return;
        var keyId = GetKeyId(e.Key.ToString());
        if (keyId == 0) return;
        SendKey(client, ControlMsgModel.AndroidKeyEventAction.Up, keyId);
    }

    public void ClearAll(ScrcpyClient? client)
    {
        if (client != null)
        {
            foreach (var pointerId in _pointerIdMap.Values)
            {
                var touchMsg = new ControlMsgModel.ControlMsg
                {
                    Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
                    Data = new ControlMsgModel.ControlMsg.InjectTouchData
                    {
                        Action = ControlMsgModel.AndroidMotionEventAction.Up,
                        PointerId = pointerId,
                        Position = new ControlMsgModel.ScPosition(0, 0, (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                        Pressure = 0.0f,
                        ActionButton = 0,
                        Buttons = 0
                    }
                };
                client.SendControlCommand(touchMsg.Serialize());
            }
        }
        _pointerIdMap.Clear();
        _nextPointerId = 0;
    }

    public void Dispose()
    {
        ClearAll(null);
    }

    private static void SendKey(ScrcpyClient client, ControlMsgModel.AndroidKeyEventAction action, int keycode)
    {
        var keyMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectKeycode,
            Data = new ControlMsgModel.InjectKeycodeData
            {
                Action = action,
                Keycode = keycode,
                Repeat = 0,
                MetaState = 0
            }
        };
        client.SendControlCommand(keyMsg.Serialize());
    }

    private static int GetKeyId(string name)
    {
        return name.ToUpper() switch
        {
            "A" => KeyCode.KEYCODE_A, "B" => KeyCode.KEYCODE_B, "C" => KeyCode.KEYCODE_C,
            "D" => KeyCode.KEYCODE_D, "E" => KeyCode.KEYCODE_E, "F" => KeyCode.KEYCODE_F,
            "G" => KeyCode.KEYCODE_G, "H" => KeyCode.KEYCODE_H, "I" => KeyCode.KEYCODE_I,
            "J" => KeyCode.KEYCODE_J, "K" => KeyCode.KEYCODE_K, "L" => KeyCode.KEYCODE_L,
            "M" => KeyCode.KEYCODE_M, "N" => KeyCode.KEYCODE_N, "O" => KeyCode.KEYCODE_O,
            "P" => KeyCode.KEYCODE_P, "Q" => KeyCode.KEYCODE_Q, "R" => KeyCode.KEYCODE_R,
            "S" => KeyCode.KEYCODE_S, "T" => KeyCode.KEYCODE_T, "U" => KeyCode.KEYCODE_U,
            "V" => KeyCode.KEYCODE_V, "W" => KeyCode.KEYCODE_W, "X" => KeyCode.KEYCODE_X,
            "Y" => KeyCode.KEYCODE_Y, "Z" => KeyCode.KEYCODE_Z,
            "0" => KeyCode.KEYCODE_0, "1" => KeyCode.KEYCODE_1, "2" => KeyCode.KEYCODE_2,
            "3" => KeyCode.KEYCODE_3, "4" => KeyCode.KEYCODE_4, "5" => KeyCode.KEYCODE_5,
            "6" => KeyCode.KEYCODE_6, "7" => KeyCode.KEYCODE_7, "8" => KeyCode.KEYCODE_8,
            "9" => KeyCode.KEYCODE_9,
            "ENTER" => KeyCode.KEYCODE_66,
            "ESCAPE" => KeyCode.KEYCODE_111,
            "SPACE" => KeyCode.KEYCODE_62,
            "SHIFT" => KeyCode.KEYCODE_59,
            "CTRL" => KeyCode.KEYCODE_113,
            "ALT" => KeyCode.KEYCODE_57,
            "BACKSPACE" => KeyCode.KEYCODE_67,
            _ => 0,
        };
    }
}