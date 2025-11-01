using System;
using System.IO;
using System.Text;

namespace AYLink.Scrcpy.Control;

#pragma warning disable CS0649

/// <summary>
/// 封装并序列化发送给scrcpy服务端的控制消息。
/// </summary>
internal class ControlMsgModel
{
    #region Enums (枚举)

    /// <summary>
    /// 定义安卓按键事件的动作。
    /// </summary>
    public enum AndroidKeyEventAction : byte
    {
        /// <summary>
        /// 按键按下。
        /// </summary>
        Down = 0,
        /// <summary>
        /// 按键弹起。
        /// </summary>
        Up = 1,
    }

    /// <summary>
    /// 定义安卓触摸/鼠标事件的动作。
    /// </summary>
    public enum AndroidMotionEventAction : byte
    {
        /// <summary>
        /// 手指或鼠标按下。
        /// </summary>
        Down = 0,
        /// <summary>
        /// 手指或鼠标抬起。
        /// </summary>
        Up = 1,
        /// <summary>
        /// 手指或鼠标移动。
        /// </summary>
        Move = 2,
        /// <summary>
        /// 事件被取消。
        /// </summary>
        Cancel = 3,
        /// <summary>
        /// 事件发生在窗口边界之外。
        /// </summary>
        Outside = 4,
        /// <summary>
        /// 另一根手指按下（多点触控）。
        /// </summary>
        PointerDown = 5,
        /// <summary>
        /// 另一根手指抬起（多点触控）。
        /// </summary>
        PointerUp = 6,
        /// <summary>
        /// 鼠标悬停移动。
        /// </summary>
        HoverMove = 7,
        /// <summary>
        /// 滚轮滚动。
        /// </summary>
        Scroll = 8,
        /// <summary>
        /// 鼠标悬停进入区域。
        /// </summary>
        HoverEnter = 9,
        /// <summary>
        /// 鼠标悬停离开区域。
        /// </summary>
        HoverExit = 10,
        /// <summary>
        /// 鼠标按钮按下（用于高级鼠标事件）。
        /// </summary>
        BtnPress = 11,
        /// <summary>
        /// 鼠标按钮释放（用于高级鼠标事件）。
        /// </summary>
        BtnRelease = 12,
    }

    /// <summary>
    /// 定义了所有可以发送给服务端的控制消息类型。
    /// 注意：枚举值必须与scrcpy服务端协议严格对应。
    /// </summary>
    public enum ControlMsgType : byte
    {
        /// <summary>
        /// 注入一个按键码（KEYCODE）。
        /// </summary>
        InjectKeycode = 0,
        /// <summary>
        /// 注入一段UTF-8文本。
        /// </summary>
        InjectText = 1,
        /// <summary>
        /// 注入一个触摸事件。用于模拟屏幕点击和滑动。
        /// </summary>
        InjectTouchEvent = 2,
        /// <summary>
        /// 注入一个滚动事件。用于模拟滚轮。
        /// </summary>
        InjectScrollEvent = 3,
        /// <summary>
        /// 触发“返回”按钮或点亮屏幕。
        /// </summary>
        BackOrScreenOn = 4,
        /// <summary>
        /// 展开通知面板（下拉状态栏）。
        /// </summary>
        ExpandNotificationPanel = 5,
        /// <summary>
        /// 展开设置面板（通常是第二次下拉状态栏）。
        /// </summary>
        ExpandSettingsPanel = 6,
        /// <summary>
        /// 收起所有面板（状态栏、通知等）。
        /// </summary>
        CollapsePanels = 7,
        /// <summary>
        /// 从设备获取剪贴板内容。
        /// </summary>
        GetClipboard = 8,
        /// <summary>
        /// 将内容设置到设备剪贴板。
        /// </summary>
        SetClipboard = 9,
        /// <summary>
        /// 设置屏幕电源模式（开、关等）。
        /// </summary>
        SetScreenPowerMode = 10,
        /// <summary>
        /// 旋转设备屏幕。
        /// </summary>
        RotateDevice = 11,
        /// <summary>
        /// 创建一个虚拟HID设备。
        /// </summary>
        UhidCreate = 12,
        /// <summary>
        /// 向虚拟HID设备输入数据。
        /// </summary>
        UhidInput = 13,
        /// <summary>
        /// 销毁一个虚拟HID设备。
        /// </summary>
        UhidDestroy = 14,
        /// <summary>
        /// 打开物理键盘设置界面。
        /// </summary>
        OpenHardKeyboardSettings = 15,
        /// <summary>
        /// 启动一个应用程序 (scrcpy v2.2+)。
        /// </summary>
        StartApp = 16,
        /// <summary>
        /// 重置视频编码器 (scrcpy v2.4+)。
        /// </summary>
        ResetVideo = 17,
    }

    /// <summary>
    /// 定义获取剪贴板时的复制/剪切操作。
    /// </summary>
    public enum CopyKey : byte
    {
        None = 0,
        Copy = 1,
        Cut = 2,
    }

    #endregion

    #region 数据结构

    /// <summary>
    /// 描述一个在屏幕上的位置。
    /// </summary>
    public struct ScPosition(int x, int y, ushort width, ushort height)
    {
        /// <summary>
        /// X坐标。
        /// </summary>
        public int X = x;
        /// <summary>
        /// Y坐标。
        /// </summary>
        public int Y = y;
        /// <summary>
        /// 屏幕宽度，用于服务端进行坐标归一化或验证。
        /// </summary>
        public ushort ScreenWidth = width;
        /// <summary>
        /// 屏幕高度，用于服务端进行坐标归一化或验证。
        /// </summary>
        public ushort ScreenHeight = height;
    }

    /// <summary>
    /// 控制消息的顶层容器。
    /// </summary>
    public class ControlMsg
    {
        /// <summary>
        /// 控制消息的最大允许大小，防止缓冲区溢出。
        /// </summary>
        private const int MaxSize = 1 << 18; // 256KB

        /// <summary>
        /// 注入文本的最大长度。
        /// </summary>
        private const int InjectTextMaxLength = 300;

        /// <summary>
        /// 设置剪贴板文本的最大长度。
        /// </summary>
        private const int ClipboardTextMaxLength = MaxSize - 14;

        /// <summary>
        /// 此消息的类型。
        /// </summary>
        public ControlMsgType Type { get; set; }

        /// <summary>
        /// 存储消息具体数据的对象，通常是一个...Data结构体。
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// 将此控制消息对象序列化为可发送到服务端的字节数组。
        /// </summary>
        /// <returns>遵循scrcpy协议的字节数组。</returns>
        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            // BinaryWriter默认使用小端字节序，需要手动处理大端字节序的写入。
            using var writer = new BinaryWriter(ms);

            // 第一个字节总是消息类型。
            writer.Write((byte)Type);

            // 根据消息类型，调用相应的序列化方法。
            switch (Type)
            {
                case ControlMsgType.InjectKeycode:
                    SerializeInjectKeycode(writer);
                    break;
                case ControlMsgType.InjectText:
                    SerializeInjectText(writer);
                    break;
                case ControlMsgType.InjectTouchEvent:
                    SerializeInjectTouchEvent(writer);
                    break;
                case ControlMsgType.InjectScrollEvent:
                    SerializeInjectScrollEvent(writer);
                    break;
                case ControlMsgType.BackOrScreenOn:
                    SerializeBackOrScreenOn(writer);
                    break;
                case ControlMsgType.GetClipboard:
                    SerializeGetClipboard(writer);
                    break;
                case ControlMsgType.SetClipboard:
                    SerializeSetClipboard(writer);
                    break;
                case ControlMsgType.SetScreenPowerMode:
                    SerializeSetScreenPowerMode(writer);
                    break;
                case ControlMsgType.UhidCreate:
                    SerializeUhidCreate(writer);
                    break;
                case ControlMsgType.UhidInput:
                    SerializeUhidInput(writer);
                    break;
                case ControlMsgType.UhidDestroy:
                    SerializeUhidDestroy(writer);
                    break;
                case ControlMsgType.StartApp:
                    SerializeStartApp(writer);
                    break;
                default:
                    if (!IsSimpleType())
                        throw new NotSupportedException($"不支持序列化此消息类型: {Type}");
                    break;
            }

            return ms.ToArray();
        }

        /// <summary>
        /// 检查消息类型是否为“简单类型”（即只有类型字节，没有数据负载）。
        /// </summary>
        private bool IsSimpleType()
        {
            return Type switch
            {
                ControlMsgType.ExpandNotificationPanel => true,
                ControlMsgType.ExpandSettingsPanel => true,
                ControlMsgType.CollapsePanels => true,
                ControlMsgType.RotateDevice => true,
                ControlMsgType.OpenHardKeyboardSettings => true,
                ControlMsgType.ResetVideo => true,
                _ => false
            };
        }

        #region Serialization Methods (序列化方法)

        private void SerializeInjectKeycode(BinaryWriter writer)
        {
            var data = (InjectKeycodeData)Data!;
            writer.Write((byte)data.Action); // 动作 (1 byte)
            WriteBigEndian(writer, (uint)data.Keycode); // 按键码 (4 bytes)
            WriteBigEndian(writer, data.Repeat); // 重复次数 (4 bytes)
            WriteBigEndian(writer, (uint)data.MetaState); // 元状态 (4 bytes)
        }
        private void SerializeInjectText(BinaryWriter writer)
        {
            var text = (string)Data!;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int length = Math.Min(bytes.Length, InjectTextMaxLength);
            WriteBigEndian(writer, (uint)length); // 文本长度 (4 bytes)
            writer.Write(bytes, 0, length); // 文本内容 (UTF-8)
        }

        private void SerializeInjectTouchEvent(BinaryWriter writer)
        {
            var data = (InjectTouchData)Data!;
            writer.Write((byte)data.Action); // 动作 (1 byte)
            WriteBigEndian(writer, data.PointerId); // 指针ID (8 bytes)
            WritePosition(writer, data.Position); // 位置 (12 bytes)
            WriteBigEndian(writer, FloatToU16(data.Pressure)); // 压力 (2 bytes, 0..1 mapped to 0..65535)
            WriteBigEndian(writer, (uint)data.ActionButton); // 触发按钮 (4 bytes)
            WriteBigEndian(writer, (uint)data.Buttons); // 当前按下的所有按钮 (4 bytes)
        }
        private void SerializeInjectScrollEvent(BinaryWriter writer)
        {
            var data = (InjectScrollData)Data!;
            WritePosition(writer, data.Position); // 位置 (12 bytes)
            WriteBigEndian(writer, FloatToI32(data.HScroll)); // 水平滚动 (4 bytes)
            WriteBigEndian(writer, FloatToI32(data.VScroll)); // 垂直滚动 (4 bytes)
            WriteBigEndian(writer, (uint)data.Buttons); // 当前按下的所有按钮 (4 bytes)
        }

        private void SerializeBackOrScreenOn(BinaryWriter writer)
        {
            var action = (AndroidKeyEventAction)Data!;
            writer.Write((byte)action); // 动作 (1 byte, 只能是Down或Up)
        }

        private void SerializeGetClipboard(BinaryWriter writer)
        {
            var copyKey = (CopyKey)Data!;
            writer.Write((byte)copyKey); // 复制/剪切键 (1 byte)
        }

        private void SerializeSetClipboard(BinaryWriter writer)
        {
            var data = (SetClipboardData)Data!;
            WriteBigEndian(writer, data.Sequence); // 序列号 (8 bytes)，用于区分请求
            writer.Write(data.Paste); // 是否同时执行粘贴操作 (1 byte)
            var bytes = Encoding.UTF8.GetBytes(data.Text ?? "");
            var length = Math.Min(bytes.Length, ClipboardTextMaxLength);
            WriteBigEndian(writer, (uint)length); // 文本长度 (4 bytes)
            writer.Write(bytes, 0, length); // 文本内容 (UTF-8)
        }

        private void SerializeSetScreenPowerMode(BinaryWriter writer)
        {
            // scrcpy协议定义了多种电源模式，但通常只用开(1)和关(0)
            // true for ON, false for OFF.
            var on = (bool)Data!;
            writer.Write(on ? (byte)1 : (byte)0); // 电源模式 (1 byte)
        }

        private void SerializeUhidCreate(BinaryWriter writer)
        {
            var data = (UhidCreateData)Data!;
            WriteBigEndian(writer, data.Id);
            var reportDescSize = (ushort)data.ReportDesc.Length;
            WriteBigEndian(writer, reportDescSize);
            writer.Write(data.ReportDesc, 0, reportDescSize);
        }

        private void SerializeUhidInput(BinaryWriter writer)
        {
            var data = (UhidInputData)Data!;
            WriteBigEndian(writer, data.Id);
            var size = (ushort)data.Data.Length;
            WriteBigEndian(writer, size);
            writer.Write(data.Data, 0, size);
        }

        private void SerializeUhidDestroy(BinaryWriter writer)
        {
            var id = (ushort)Data!;
            WriteBigEndian(writer, id);
        }

        private void SerializeStartApp(BinaryWriter writer)
        {
            var name = (string)Data!;
            var bytes = Encoding.UTF8.GetBytes(name); // 应用包名是UTF-8
            var length = Math.Min(bytes.Length, 255);
            writer.Write((byte)length); // 长度 (1 byte)
            writer.Write(bytes, 0, length); // 包名
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 将一个位置结构体写入二进制流。
        /// </summary>
        private void WritePosition(BinaryWriter writer, ScPosition pos)
        {
            WriteBigEndian(writer, (uint)pos.X);
            WriteBigEndian(writer, (uint)pos.Y);
            WriteBigEndian(writer, pos.ScreenWidth);
            WriteBigEndian(writer, pos.ScreenHeight);
        }

        /// <summary>
        /// 将一个值以大端字节序（网络字节序）写入二进制流。
        /// </summary>
        private void WriteBigEndian(BinaryWriter writer, uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private void WriteBigEndian(BinaryWriter writer, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private void WriteBigEndian(BinaryWriter writer, ushort value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private void WriteBigEndian(BinaryWriter writer, ulong value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            writer.Write(bytes);
        }

        /// <summary>
        /// 将范围[0.0, 1.0]的浮点数转换为ushort(0-65535)的定点数。
        /// </summary>
        private static ushort FloatToU16(float value)
        {
            value = Math.Max(0f, Math.Min(value, 1f)); // Clamp
            return (ushort)(value * ushort.MaxValue);
        }

        /// <summary>
        /// 将浮点数转换为32位定点数，乘以 1<<16。
        /// </summary>
        private static int FloatToI32(float value)
        {
            return (int)(value * (1 << 16));
        }

        internal class InjectTouchData
        {
            public AndroidMotionEventAction Action { get; set; }
            public ulong PointerId { get; set; }
            public ScPosition Position { get; set; }
            public float Pressure { get; set; }
            public int ActionButton { get; set; }
            public int Buttons { get; set; }
        }

        #endregion
    }

    #region Data Payloads (数据负载结构体)

    /// <summary>
    /// `InjectKeycode` 消息的数据。
    /// </summary>
    public struct InjectKeycodeData
    {
        public AndroidKeyEventAction Action;
        public int Keycode; // Android Keycode
        public uint Repeat;
        public int MetaState; // Android MetaState
    }

    /// <summary>
    /// `InjectScrollEvent` 消息的数据。
    /// </summary>
    public struct InjectScrollData
    {
        public ScPosition Position;
        public float HScroll; // 水平滚动量
        public float VScroll; // 垂直滚动量
        public int Buttons; // 当前所有按下的按钮的状态掩码
    }

    /// <summary>
    /// `SetClipboard` 消息的数据。
    /// </summary>
    public struct SetClipboardData
    {
        public ulong Sequence; // 用于标识请求的唯一序列号
        public string Text;
        public bool Paste; // 是否在设置后立即执行粘贴操作
    }

    /// <summary>
    /// `UhidCreate` 消息的数据。
    /// </summary>
    public struct UhidCreateData
    {
        public ushort Id; // 设备ID
        public byte[] ReportDesc; // HID报告描述符
    }

    /// <summary>
    /// `UhidInput` 消息的数据。
    /// </summary>
    public struct UhidInputData
    {
        public ushort Id; // 设备ID
        public byte[] Data; // 要发送的HID数据
    }

    #endregion
}
#endregion

#pragma warning restore CS0649