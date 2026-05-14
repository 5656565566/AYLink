using AYLink.Core.Scrcpy;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace AYLink.Desktop.Models;

public class DeviceConfig
{
    /// <summary>
    /// 请求服务端发送视频流设置为false时 服务端不启动视频编码
    /// </summary>
    public bool Video { get; set; } = true;

    /// <summary>
    /// 请求服务端发送音频流需要Android 11+设置为false时 服务端不捕获音频
    /// </summary>
    public bool Audio { get; set; } = true;

    /// <summary>
    /// 请求服务端使用的视频编解码器 (h264, h265, av1)
    /// </summary>
    public string VideoCodec { get; set; } = "h264";

    /// <summary>
    /// 请求服务端使用的音频编解码器 (opus, aac, flac, raw)
    /// </summary>
    public string AudioCodec { get; set; } = "opus";

    /// <summary>
    /// 请求视频源'display'表示屏幕内容 'camera'表示摄像头
    /// </summary>
    public string VideoSource { get; set; } = "display";

    /// <summary>
    /// 请求音频源'output'表示设备内部播放的声音 'mic'表示麦克风
    /// </summary>
    public string AudioSource { get; set; } = "output";

    /// <summary>
    /// 请求服务端在捕获音频的同时 也将其路由到设备扬声器播放
    /// </summary>
    public bool AudioDup { get; set; } = false;

    /// <summary>
    /// 请求服务端使用的特定视频编码器名称如果设置 将忽略VideoCodec
    /// </summary>
    public string? VideoEncoder { get; set; }

    /// <summary>
    /// 请求服务端使用的特定音频编码器名称如果设置 将忽略AudioCodec
    /// </summary>
    public string? AudioEncoder { get; set; }

    /// <summary>
    /// 为音视频编码器设置高级键值对选项
    /// 格式: "key:type=value,key2:type=value..."
    /// </summary>
    public string? CodecOptions { get; set; }

    /// <summary>
    /// 请求服务端使用的视频比特率 (单位: bps)例如: 8000000 (8Mbps)
    /// </summary>
    public int? VideoBitRate { get; set; }

    /// <summary>
    /// 最大分辨率 不影响屏幕比例 缩放最长的边 如 1920 压缩 3200x1440 为 1920 x 864
    /// </summary>
    public int? MaxSize { get; set; }

    /// <summary>
    /// 请求服务端使用的音频比特率 (单位: bps)例如: 128000 (128kbps)
    /// </summary>
    public int? AudioBitRate { get; set; }

    /// <summary>
    /// 请求服务端编码的最大帧率
    /// </summary>
    public float? MaxFps { get; set; }
    /// <summary>
    /// 请求服务端监听并处理控制事件（如点击、按键）设置为false则为纯观看模式
    /// </summary>
    public bool Control { get; set; } = true;

    /// <summary>
    /// 请求服务端开启“显示触摸操作”的开发者选项功能
    /// </summary>
    public bool ShowTouches { get; set; } = false;

    /// <summary>
    /// 请求服务端持有一个Wakelock 防止设备在连接期间自动休眠
    /// </summary>
    public bool StayAwake { get; set; } = false;

    /// <summary>
    /// 启动时 请求服务端唤醒并点亮屏幕
    /// </summary>
    public bool PowerOn { get; set; } = true;

    /// <summary>
    /// 客户端断开连接时 请求服务端执行关闭屏幕操作
    /// </summary>
    public bool PowerOffOnClose { get; set; } = false;

    /// <summary>
    /// 设置一个延迟（毫秒） 如果客户端在这段时间内没有交互 则关闭设备屏幕-1表示禁用
    /// </summary>
    public int? ScreenOffTimeout { get; set; }

    /// <summary>
    /// 请求服务端使用 HID 键盘进行输入需要 Android 12+
    /// </summary>
    public bool HidKeyboard { get; set; } = false;

    /// <summary>
    /// 请求服务端使用 HID 鼠标进行输入需要 Android 12+
    /// </summary>
    public bool HidMouse { get; set; } = false;

    /// <summary>
    /// 要使用的摄像头ID
    /// </summary>
    public string? CameraId { get; set; }

    /// <summary>
    /// 期望的摄像头分辨率
    /// </summary>
    public Size? CameraSize { get; set; }

    /// <summary>
    /// 选择摄像头朝向 (front, back, external)
    /// </summary>
    public string? CameraFacing { get; set; }

    /// <summary>
    /// 期望的摄像头宽高比
    /// </summary>
    public string? CameraAspectRatio { get; set; }

    /// <summary>
    /// 期望的摄像头帧率
    /// </summary>
    public int? CameraFps { get; set; }

    /// <summary>
    /// 是否启用摄像头高速模式
    /// </summary>
    public bool CameraHighSpeed { get; set; } = false;

    /// <summary>
    /// 创建显示器
    /// </summary>
    public string? NewDisplay { get; set; }

    /// <summary>
    /// 弹性显示器 (客户端调整窗口大小时，动态调整虚拟显示器的大小)
    /// </summary>
    public bool FlexDisplay { get; set; } = false;

    /// <summary>
    /// 销毁内容
    /// </summary>
    public bool VdDestroyContent { get; set; } = true;

    /// <summary>
    /// 系统主题
    /// </summary>
    public bool VdSystemDecorations { get; set; } = true;
}

public static class DeviceConfigExtensions
{
    public static void ApplyConfig(this DeviceConfig config, ServerOptions options)
    {
        var sourceProperties = typeof(DeviceConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var destinationProperties = typeof(ServerOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                          .ToDictionary(p => p.Name);

        foreach (var sourceProp in sourceProperties)
        {
            if (destinationProperties.TryGetValue(sourceProp.Name, out var destProp))
            {
                if (destProp.CanWrite && destProp.PropertyType == sourceProp.PropertyType)
                {
                    var value = sourceProp.GetValue(config);
                    destProp.SetValue(options, value);
                }
            }
        }
    }
}
