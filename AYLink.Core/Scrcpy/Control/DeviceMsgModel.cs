using System;
using System.IO;
using System.Text;

namespace AYLink.Core.Scrcpy.Control;

/// <summary>
/// 定义安卓设备发向PC端的设备消息类型
/// </summary>
public enum DeviceMsgType : byte
{
    /// <summary>
    /// 手机端剪贴板内容同步
    /// </summary>
    Clipboard = 0,
    
    /// <summary>
    /// 确认已成功设置手机端剪贴板
    /// </summary>
    AckSetClipboard = 1,
    
    /// <summary>
    /// 虚拟HID设备的输出数据
    /// </summary>
    UhidOutput = 2,
}

/// <summary>
/// 封装并反序列化接收自scrcpy服务端的设备消息
/// </summary>
public class DeviceMsg
{
    /// <summary>
    /// 此消息的类型
    /// </summary>
    public DeviceMsgType Type { get; set; }

    /// <summary>
    /// 存储消息具体数据
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// 从二进制流中反序列化设备消息
    /// </summary>
    /// <param name="reader">连接了控制套接字的 BinaryReader</param>
    /// <returns>反序列化后的消息对象；若到达流末尾或无法识别则返回 null</returns>
    public static DeviceMsg? Deserialize(BinaryReader reader)
    {
        try
        {
            byte typeByte = reader.ReadByte();
            if (!Enum.IsDefined(typeof(DeviceMsgType), typeByte))
            {
                return null;
            }

            var type = (DeviceMsgType)typeByte;
            var msg = new DeviceMsg { Type = type };

            switch (type)
            {
                case DeviceMsgType.Clipboard:
                    int len = ReadBigEndianInt32(reader);
                    if (len > 0)
                    {
                        byte[] textBytes = reader.ReadBytes(len);
                        msg.Data = Encoding.UTF8.GetString(textBytes);
                    }
                    else
                    {
                        msg.Data = string.Empty;
                    }
                    break;

                case DeviceMsgType.AckSetClipboard:
                    ulong seq = ReadBigEndianUInt64(reader);
                    msg.Data = seq;
                    break;

                case DeviceMsgType.UhidOutput:
                    ushort id = ReadBigEndianUInt16(reader);
                    ushort size = ReadBigEndianUInt16(reader);
                    byte[] data = reader.ReadBytes(size);
                    msg.Data = new UhidOutputData { Id = id, Data = data };
                    break;
            }

            return msg;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (IOException)
        {
            return null; // Socket 关闭时通常会抛出 IOException
        }
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) throw new EndOfStreamException();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }

    private static ushort ReadBigEndianUInt16(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(2);
        if (bytes.Length < 2) throw new EndOfStreamException();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt16(bytes, 0);
    }

    private static ulong ReadBigEndianUInt64(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(8);
        if (bytes.Length < 8) throw new EndOfStreamException();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }

    /// <summary>
    /// `UhidOutput` 消息的数据负载结构
    /// </summary>
    public struct UhidOutputData
    {
        public ushort Id;
        public byte[] Data;
    }
}
