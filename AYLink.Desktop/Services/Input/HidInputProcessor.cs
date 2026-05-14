using Avalonia;
using AYLink.Core.Scrcpy.Control;
using System;
using System.Collections.Generic;

namespace AYLink.Desktop.Services.Input;

/// <summary>
/// HID 输入处理器
/// 将输入事件转换为底层的 USB HID 报文并通过 UhidInput 消息发送给设备
/// 需要 Android 12+ 并开启相应的选项
/// </summary>
public class HidInputProcessor : IInputProcessor
{
    private Size _screenSize;
    private IControlCommandSender? _sender;
    private readonly bool _hidKeyboardEnabled;
    private readonly bool _hidMouseEnabled;

    private readonly DefaultInputProcessor _fallbackProcessor = new();

    // 分配的虚拟设备 ID
    private const ushort MOUSE_ID = 1;
    private const ushort KEYBOARD_ID = 2;

    private bool _isMouseCreated;
    private bool _isKeyboardCreated;

    // 当前鼠标按键状态 (位掩码)
    private byte _currentMouseButtons = 0;

    // 记录按下的按键以便在 ClearAll 中释放
    private readonly HashSet<byte> _pressedKeys = [];

    private bool _isCursorLocked;
    public event Action<bool>? CursorLockRequested;

    public void SetCursorLocked(bool locked)
    {
        _isCursorLocked = locked;
        _fallbackProcessor.SetCursorLocked(locked);
    }

    public HidInputProcessor(bool hidKeyboardEnabled, bool hidMouseEnabled)
    {
        _hidKeyboardEnabled = hidKeyboardEnabled;
        _hidMouseEnabled = hidMouseEnabled;
    }

    public void SetSender(IControlCommandSender? sender)
    {
        _sender = sender;
        _fallbackProcessor.SetSender(sender);
    }

    public void CreateDevices()
    {
        if (_sender == null) return;

        if (_hidMouseEnabled && !_isMouseCreated)
        {
            var msg = new ControlMsgModel.ControlMsg
            {
                Type = ControlMsgModel.ControlMsgType.UhidCreate,
                Data = new ControlMsgModel.UhidCreateData
                {
                    Id = MOUSE_ID,
                    ReportDesc = RelativeMouseReportDesc
                }
            };
            _sender.SendCommand(msg.Serialize());
            _isMouseCreated = true;
        }

        if (_hidKeyboardEnabled && !_isKeyboardCreated)
        {
            var msg = new ControlMsgModel.ControlMsg
            {
                Type = ControlMsgModel.ControlMsgType.UhidCreate,
                Data = new ControlMsgModel.UhidCreateData
                {
                    Id = KEYBOARD_ID,
                    ReportDesc = KeyboardReportDesc
                }
            };
            _sender.SendCommand(msg.Serialize());
            _isKeyboardCreated = true;
        }
    }

    public void UpdateScreenSize(Size size)
    {
        _screenSize = size;
        _fallbackProcessor.UpdateScreenSize(size);
    }

    public void ProcessPointer(PointerInput input)
    {
        // 如果未开启 HID 鼠标，或者未准备好，回退到默认的触摸模拟注入
        if (!_hidMouseEnabled || _sender == null || !_isMouseCreated)
        {
            _fallbackProcessor.ProcessPointer(input);
            return;
        }

        // 未锁定鼠标时，投屏区域按普通触摸模式处理。
        // 这样点击/拖动都会直接映射到屏幕坐标，避免 HID 相对鼠标在未锁定时只发送按键、
        // 却没有稳定位置更新，导致点击落到设备当前鼠标光标位置。
        if (!_isCursorLocked)
        {
            _fallbackProcessor.ProcessPointer(input);
            return;
        }

        int dx = 0;
        int dy = 0;
        int vWheel = 0;
        int hWheel = 0;

        switch (input.EventType)
        {
            case PointerEventType.Pressed:
                _currentMouseButtons |= 0x01; // 左键
                break;

            case PointerEventType.Released:
            case PointerEventType.CaptureLost:
                _currentMouseButtons &= 0xFE; // 取消左键
                break;

            case PointerEventType.WheelChanged:
                // 滚轮事件，限定在 -127 到 127 之间
                vWheel = (int)input.WheelDelta.Y;
                hWheel = (int)input.WheelDelta.X;
                break;

            case PointerEventType.Moved:
                if (input.HasRelativeDelta)
                {
                    double sensitivity = _isCursorLocked ? 1.0 : 1.5;
                    dx = (int)Math.Round(input.RelativeDelta.X * sensitivity);
                    dy = (int)Math.Round(input.RelativeDelta.Y * sensitivity);
                }
                else
                {
                    return; // 第一次无增量
                }
                break;
        }

        // 截断到 sbyte 的范围
        if (dx > 127) dx = 127; if (dx < -127) dx = -127;
        if (dy > 127) dy = 127; if (dy < -127) dy = -127;
        if (vWheel > 127) vWheel = 127; if (vWheel < -127) vWheel = -127;
        if (hWheel > 127) hWheel = 127; if (hWheel < -127) hWheel = -127;

        SendMouseReport(_currentMouseButtons, (sbyte)dx, (sbyte)dy, (sbyte)vWheel, (sbyte)hWheel);
    }

    private void SendMouseReport(byte buttons, sbyte dx, sbyte dy, sbyte vWheel, sbyte hWheel)
    {
        byte[] report = new byte[5];
        report[0] = buttons;
        report[1] = (byte)dx;
        report[2] = (byte)dy;
        report[3] = (byte)vWheel;
        report[4] = (byte)hWheel;

        var msg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.UhidInput,
            Data = new ControlMsgModel.UhidInputData
            {
                Id = MOUSE_ID,
                Data = report
            }
        };
        _sender?.SendCommand(msg.Serialize());
    }

    public void ProcessKey(KeyInput input)
    {
        // 捕获 ALT 键用于切换鼠标锁定
        if (input.KeyName.ToUpper() == "LEFTALT" || input.KeyName.ToUpper() == "RIGHTALT" || input.KeyName.ToUpper() == "ALT")
        {
            if (input.EventType == KeyEventType.Down)
            {
                // 触发光标锁定状态切换
                _isCursorLocked = !_isCursorLocked;
                CursorLockRequested?.Invoke(_isCursorLocked);
            }
            // 不要把 Alt 键发送给设备，以免引起设备端焦点丢失或菜单弹出
            return;
        }

        // 如果未开启 HID 键盘，回退到默认的按键模拟注入
        if (!_hidKeyboardEnabled || _sender == null || !_isKeyboardCreated)
        {
            _fallbackProcessor.ProcessKey(input);
            return;
        }

        byte hidKey = GetHidKeyCode(input.KeyName);
        if (hidKey == 0) return;

        if (input.EventType == KeyEventType.Down)
        {
            _pressedKeys.Add(hidKey);
        }
        else
        {
            _pressedKeys.Remove(hidKey);
        }

        SendKeyboardReport();
    }

    private void SendKeyboardReport()
    {
        // HID 键盘标准报告 (8字节):
        // Byte 0: Modifier keys (Ctrl, Shift, Alt, GUI)
        // Byte 1: Reserved (0)
        // Byte 2-7: Pressed keys (up to 6)

        byte modifiers = 0;
        byte[] report = new byte[8];

        int keyIndex = 2;
        foreach (var key in _pressedKeys)
        {
            // 处理修饰键
            if (key >= 0xE0 && key <= 0xE7)
            {
                modifiers |= (byte)(1 << (key - 0xE0));
            }
            else
            {
                if (keyIndex < 8)
                {
                    report[keyIndex++] = key;
                }
            }
        }

        report[0] = modifiers;

        var msg = new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.UhidInput,
            Data = new ControlMsgModel.UhidInputData
            {
                Id = KEYBOARD_ID,
                Data = report
            }
        };
        _sender?.SendCommand(msg.Serialize());
    }

    public void ClearAll()
    {
        _fallbackProcessor.ClearAll();

        if (_sender == null) return;

        if (_hidMouseEnabled && _isMouseCreated && _currentMouseButtons != 0)
        {
            _currentMouseButtons = 0;
            SendMouseReport(_currentMouseButtons, 0, 0, 0, 0); 
        }

        if (_hidKeyboardEnabled && _isKeyboardCreated && _pressedKeys.Count > 0)
        {
            _pressedKeys.Clear();
            SendKeyboardReport();
        }
    }

    public void Dispose()
    {
        ClearAll();
        _fallbackProcessor.Dispose();

        if (_sender != null)
        {
            if (_isMouseCreated)
            {
                _sender.SendCommand(new ControlMsgModel.ControlMsg
                {
                    Type = ControlMsgModel.ControlMsgType.UhidDestroy,
                    Data = MOUSE_ID
                }.Serialize());
                _isMouseCreated = false;
            }

            if (_isKeyboardCreated)
            {
                _sender.SendCommand(new ControlMsgModel.ControlMsg
                {
                    Type = ControlMsgModel.ControlMsgType.UhidDestroy,
                    Data = KEYBOARD_ID
                }.Serialize());
                _isKeyboardCreated = false;
            }
        }

        _sender = null;
    }

    /// <summary>
    /// 将 Avalonia Key 名称转换为 USB HID 键盘 Scan Code
    /// </summary>
    private static byte GetHidKeyCode(string name)
    {
        return name.ToUpper() switch
        {
            "A" => 0x04, "B" => 0x05, "C" => 0x06, "D" => 0x07, "E" => 0x08, "F" => 0x09,
            "G" => 0x0A, "H" => 0x0B, "I" => 0x0C, "J" => 0x0D, "K" => 0x0E, "L" => 0x0F,
            "M" => 0x10, "N" => 0x11, "O" => 0x12, "P" => 0x13, "Q" => 0x14, "R" => 0x15,
            "S" => 0x16, "T" => 0x17, "U" => 0x18, "V" => 0x19, "W" => 0x1A, "X" => 0x1B,
            "Y" => 0x1C, "Z" => 0x1D,
            "D1" or "1" => 0x1E, "D2" or "2" => 0x1F, "D3" or "3" => 0x20, "D4" or "4" => 0x21,
            "D5" or "5" => 0x22, "D6" or "6" => 0x23, "D7" or "7" => 0x24, "D8" or "8" => 0x25,
            "D9" or "9" => 0x26, "D0" or "0" => 0x27,
            "ENTER" or "RETURN" => 0x28,
            "ESCAPE" => 0x29,
            "BACK" or "BACKSPACE" => 0x2A,
            "TAB" => 0x2B,
            "SPACE" => 0x2C,
            "MINUS" or "OEMMINUS" => 0x2D,
            "EQUAL" or "OEMPLUS" => 0x2E,
            "LEFTBRACKET" or "OEMOPENBRACKETS" => 0x2F,
            "RIGHTBRACKET" or "OEMCLOSEBRACKETS" => 0x30,
            "BACKSLASH" or "OEMPIPE" => 0x31,
            "SEMICOLON" or "OEMSEMICOLON" => 0x33,
            "APOSTROPHE" or "OEMQUOTES" => 0x34,
            "GRAVE" or "OEMTILDE" => 0x35,
            "COMMA" or "OEMCOMMA" => 0x36,
            "PERIOD" or "OEMPERIOD" => 0x37,
            "SLASH" or "OEMQUESTION" => 0x38,
            "CAPSLOCK" => 0x39,
            "F1" => 0x3A, "F2" => 0x3B, "F3" => 0x3C, "F4" => 0x3D, "F5" => 0x3E, "F6" => 0x3F,
            "F7" => 0x40, "F8" => 0x41, "F9" => 0x42, "F10" => 0x43, "F11" => 0x44, "F12" => 0x45,
            "RIGHT" => 0x4F, "LEFT" => 0x50, "DOWN" => 0x51, "UP" => 0x52,
            "LEFTCTRL" => 0xE0, "LEFTSHIFT" => 0xE1, "LEFTALT" => 0xE2, "LWIN" => 0xE3,
            "RIGHTCTRL" => 0xE4, "RIGHTSHIFT" => 0xE5, "RIGHTALT" => 0xE6, "RWIN" => 0xE7,
            "CTRL" => 0xE0, "SHIFT" => 0xE1, "ALT" => 0xE2, // 兼容简化名称
            _ => 0
        };
    }

    // 标准相对位移鼠标 (符合 USB HID Spec)
    private static readonly byte[] RelativeMouseReportDesc = [
        0x05, 0x01, // Usage Page (Generic Desktop Ctrls)
        0x09, 0x02, // Usage (Mouse)
        0xA1, 0x01, // Collection (Application)
        0x09, 0x01, //   Usage (Pointer)
        0xA1, 0x00, //   Collection (Physical)
        // 5 buttons
        0x05, 0x09, //     Usage Page (Button)
        0x19, 0x01, //     Usage Minimum (0x01)
        0x29, 0x05, //     Usage Maximum (0x05)
        0x15, 0x00, //     Logical Minimum (0)
        0x25, 0x01, //     Logical Maximum (1)
        0x95, 0x05, //     Report Count (5)
        0x75, 0x01, //     Report Size (1)
        0x81, 0x02, //     Input (Data,Var,Abs,No Wrap,Linear,Preferred State,No Null Position)
        // 3 bits padding
        0x95, 0x01, //     Report Count (1)
        0x75, 0x03, //     Report Size (3)
        0x81, 0x01, //     Input (Const,Array,Abs,No Wrap,Linear,Preferred State,No Null Position)
        // X, Y, Wheel (Relative)
        0x05, 0x01, //     Usage Page (Generic Desktop Ctrls)
        0x09, 0x30, //     Usage (X)
        0x09, 0x31, //     Usage (Y)
        0x09, 0x38, //     Usage (Wheel)
        0x15, 0x81, //     Logical Minimum (-127)
        0x25, 0x7F, //     Logical Maximum (127)
        0x75, 0x08, //     Report Size (8)
        0x95, 0x03, //     Report Count (3)
        0x81, 0x06, //     Input (Data,Var,Rel,No Wrap,Linear,Preferred State,No Null Position)
        // AC Pan (Horizontal scroll)
        0x05, 0x0C, //     Usage Page (Consumer)
        0x0A, 0x38, 0x02, // Usage (AC Pan)
        0x15, 0x81, //     Logical Minimum (-127)
        0x25, 0x7F, //     Logical Maximum (127)
        0x75, 0x08, //     Report Size (8)
        0x95, 0x01, //     Report Count (1)
        0x81, 0x06, //     Input (Data,Var,Rel,No Wrap,Linear,Preferred State,No Null Position)
        0xC0,       //   End Collection
        0xC0        // End Collection
    ];

    // 标准键盘
    private static readonly byte[] KeyboardReportDesc = [
        0x05, 0x01, // Usage Page (Generic Desktop Ctrls)
        0x09, 0x06, // Usage (Keyboard)
        0xA1, 0x01, // Collection (Application)
        0x05, 0x07, //   Usage Page (Kbrd/Keypad)
        0x19, 0xE0, //   Usage Minimum (0xE0)
        0x29, 0xE7, //   Usage Maximum (0xE7)
        0x15, 0x00, //   Logical Minimum (0)
        0x25, 0x01, //   Logical Maximum (1)
        0x75, 0x01, //   Report Size (1)
        0x95, 0x08, //   Report Count (8)
        0x81, 0x02, //   Input (Data,Var,Abs,No Wrap,Linear,Preferred State,No Null Position)
        0x95, 0x01, //   Report Count (1)
        0x75, 0x08, //   Report Size (8)
        0x81, 0x01, //   Input (Const,Array,Abs,No Wrap,Linear,Preferred State,No Null Position)
        0x95, 0x05, //   Report Count (5)
        0x75, 0x01, //   Report Size (1)
        0x05, 0x08, //   Usage Page (LEDs)
        0x19, 0x01, //   Usage Minimum (Num Lock)
        0x29, 0x05, //   Usage Maximum (Kana)
        0x91, 0x02, //   Output (Data,Var,Abs,No Wrap,Linear,Preferred State,No Null Position,Non-volatile)
        0x95, 0x01, //   Report Count (1)
        0x75, 0x03, //   Report Size (3)
        0x91, 0x01, //   Output (Const,Array,Abs,No Wrap,Linear,Preferred State,No Null Position,Non-volatile)
        0x95, 0x06, //   Report Count (6)
        0x75, 0x08, //   Report Size (8)
        0x15, 0x00, //   Logical Minimum (0)
        0x25, 0x65, //   Logical Maximum (101)
        0x05, 0x07, //   Usage Page (Kbrd/Keypad)
        0x19, 0x00, //   Usage Minimum (0x00)
        0x29, 0x65, //   Usage Maximum (0x65)
        0x81, 0x00, //   Input (Data,Array,Abs,No Wrap,Linear,Preferred State,No Null Position)
        0xC0        // End Collection
    ];
}
