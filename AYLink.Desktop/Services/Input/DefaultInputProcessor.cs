using Avalonia;
using AYLink.Core.Scrcpy.Control;
using System;
using System.Collections.Generic;

namespace AYLink.Desktop.Services.Input;

/// <summary>
/// 默认的注入模式输入处理器
/// 负责将抽象输入事件转换为 Android 的触摸和按键事件并发送
/// </summary>
public class DefaultInputProcessor : IInputProcessor
{
    private Size _screenSize;
    private readonly Dictionary<int, ulong> _pointerIdMap = [];
    private ulong _nextPointerId;
    private IControlCommandSender? _sender;

    event Action<bool>? IInputProcessor.CursorLockRequested
    {
        add { }
        remove { }
    }

    public void SetCursorLocked(bool locked)
    {
        // 默认注入模式不需要处理光标锁定状态
    }

    public void SetSender(IControlCommandSender? sender)
    {
        _sender = sender;
    }

    public void UpdateScreenSize(Size size)
    {
        _screenSize = size;
    }

    public void ProcessPointer(PointerInput input)
    {
        if (_sender == null) return;

        switch (input.EventType)
        {
            case PointerEventType.Pressed:
                ProcessPointerPressed(input);
                break;
            case PointerEventType.Moved:
                ProcessPointerMoved(input);
                break;
            case PointerEventType.Released:
                ProcessPointerReleased(input);
                break;
            case PointerEventType.WheelChanged:
                ProcessPointerWheelChanged(input);
                break;
            case PointerEventType.CaptureLost:
                ProcessPointerCaptureLost(input);
                break;
        }
    }

    public void ProcessKey(KeyInput input)
    {
        if (_sender == null) return;
        var keyId = GetKeyId(input.KeyName);
        if (keyId == 0) return;

        SendKey(input.EventType == KeyEventType.Down ? ControlMsgModel.AndroidKeyEventAction.Down : ControlMsgModel.AndroidKeyEventAction.Up, keyId);
    }

    private void ProcessPointerPressed(PointerInput input)
    {
        if (!_pointerIdMap.TryGetValue(input.PointerHash, out ulong pointerId))
        {
            pointerId = _nextPointerId++;
            _pointerIdMap[input.PointerHash] = pointerId;
        }

        var touchMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
            Data = new ControlMsgModel.ControlMsg.InjectTouchData
            {
                Action = ControlMsgModel.AndroidMotionEventAction.Down,
                PointerId = pointerId,
                Position = new ControlMsgModel.ScPosition(
                    (int)input.Position.X, (int)input.Position.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                Pressure = 1.0f,
                ActionButton = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY,
                Buttons = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY
            }
        };

        _sender?.SendCommand(touchMsg.Serialize());
    }

    private void ProcessPointerMoved(PointerInput input)
    {
        if (!_pointerIdMap.TryGetValue(input.PointerHash, out ulong pointerId)) return;

        var touchMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
            Data = new ControlMsgModel.ControlMsg.InjectTouchData
            {
                Action = ControlMsgModel.AndroidMotionEventAction.Move,
                PointerId = pointerId,
                Position = new ControlMsgModel.ScPosition(
                    (int)input.Position.X, (int)input.Position.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                Pressure = 1.0f,
                ActionButton = 0,
                Buttons = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY
            }
        };

        _sender?.SendCommand(touchMsg.Serialize());
    }

    private void ProcessPointerReleased(PointerInput input)
    {
        if (!_pointerIdMap.TryGetValue(input.PointerHash, out ulong pointerId)) return;

        var touchMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectTouchEvent,
            Data = new ControlMsgModel.ControlMsg.InjectTouchData
            {
                Action = ControlMsgModel.AndroidMotionEventAction.Up,
                PointerId = pointerId,
                Position = new ControlMsgModel.ScPosition(
                    (int)input.Position.X, (int)input.Position.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                Pressure = 0.0f,
                ActionButton = (int)KeyCode.AndroidMotionEventButton.BUTTON_PRIMARY,
                Buttons = 0
            }
        };

        _sender?.SendCommand(touchMsg.Serialize());
        _pointerIdMap.Remove(input.PointerHash);
    }

    private void ProcessPointerWheelChanged(PointerInput input)
    {
        var controlMsg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.InjectScrollEvent,
            Data = new ControlMsgModel.InjectScrollData
            {
                Position = new ControlMsgModel.ScPosition(
                    (int)input.Position.X, (int)input.Position.Y,
                    (ushort)_screenSize.Width, (ushort)_screenSize.Height),
                VScroll = (float)-input.WheelDelta.Y,
                HScroll = (float)-input.WheelDelta.X,
                Buttons = 0
            }
        };
        _sender?.SendCommand(controlMsg.Serialize());
    }

    private void ProcessPointerCaptureLost(PointerInput input)
    {
        if (_pointerIdMap.TryGetValue(input.PointerHash, out ulong pointerId))
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
            _sender?.SendCommand(touchMsg.Serialize());
            _pointerIdMap.Remove(input.PointerHash);
        }
    }


    public void ClearAll()
    {
        if (_sender != null)
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
                _sender.SendCommand(touchMsg.Serialize());
            }
        }
        _pointerIdMap.Clear();
        _nextPointerId = 0;
    }

    public void Dispose()
    {
        ClearAll();
        _sender = null;
    }

    private void SendKey(ControlMsgModel.AndroidKeyEventAction action, int keycode)
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
        _sender?.SendCommand(keyMsg.Serialize());
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
