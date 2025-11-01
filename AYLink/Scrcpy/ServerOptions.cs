using Avalonia.Controls;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace AYLink.Scrcpy;

/// <summary>
/// 生成 scrcpy-server.jar 的启动参数。
/// </summary>
public class ServerOptions
{
    #region 核心参数

    /// <summary>
    /// 服务端必须知道与之通信的客户端版本，以确保协议兼容。
    /// </summary>
    public string ClientVersion { get; set; } = "3.3.3";

    /// <summary>
    /// 服务端连接ID (Session ID)，用于区分连接到同一个服务端的多个客户端。
    /// </summary>
    public int? Scid { get; set; }

    /// <summary>
    /// 服务端输出的日志级别 (debug, info, warn, error)。日志将在adb logcat中可见。
    /// </summary>
    public string LogLevel { get; set; } = "info";

    /// <summary>
    /// 请求服务端发送视频流。设置为false时，服务端不启动视频编码。
    /// </summary>
    public bool Video { get; set; } = true;

    /// <summary>
    /// 请求服务端发送音频流。需要Android 11+。设置为false时，服务端不捕获音频。
    /// </summary>
    public bool Audio { get; set; } = true;

    #endregion

    #region 编码和源

    /// <summary>
    /// 请求服务端使用的视频编解码器 (h264, h265, av1)。
    /// </summary>
    public string VideoCodec { get; set; } = "h264";

    /// <summary>
    /// 请求服务端使用的音频编解码器 (opus, aac, flac, raw)。
    /// </summary>
    public string AudioCodec { get; set; } = "opus";

    /// <summary>
    /// 请求视频源。'display'表示屏幕内容，'camera'表示摄像头。
    /// </summary>
    public string VideoSource { get; set; } = "display";

    /// <summary>
    /// 请求音频源。'output'表示设备内部播放的声音，'mic'表示麦克风。
    /// </summary>
    public string AudioSource { get; set; } = "output";

    /// <summary>
    /// 请求服务端在捕获音频的同时，也将其路由到设备扬声器播放。
    /// </summary>
    public bool AudioDup { get; set; } = false;

    /// <summary>
    /// 请求服务端使用的特定视频编码器名称。如果设置，将忽略VideoCodec。
    /// </summary>
    public string? VideoEncoder { get; set; }

    /// <summary>
    /// 请求服务端使用的特定音频编码器名称。如果设置，将忽略AudioCodec。
    /// </summary>
    public string? AudioEncoder { get; set; }

    /// <summary>
    /// 为音视频编码器设置高级键值对选项。
    /// 格式: "key:type=value,key2:type=value..."
    /// </summary>
    public string? CodecOptions { get; set; }

    #endregion

    #region 媒体参数

    /// <summary>
    /// 创建显示器
    /// </summary>
    public string? NewDisplay { get; set; }

    /// <summary>
    /// 销毁内容
    /// </summary>
    /// 
    public bool VdDestroyContent { get; set; } = true;
    /// <summary>
    /// 系统主题
    /// </summary>
    public bool VdSystemDecorations { get; set; } = true;

    /// <summary>
    /// 限制服务端编码的视频分辨率（宽度和高度中的最大值）。
    /// </summary>
    public int? MaxSize { get; set; }

    /// <summary>
    /// 请求服务端使用的视频比特率 (单位: bps)。例如: 8000000 (8Mbps)。
    /// </summary>
    public int? VideoBitRate { get; set; }

    /// <summary>
    /// 请求服务端使用的音频比特率 (单位: bps)。例如: 128000 (128kbps)。
    /// </summary>
    public int? AudioBitRate { get; set; }

    /// <summary>
    /// 请求服务端编码的最大帧率。
    /// </summary>
    public float? MaxFps { get; set; }

    /// <summary>
    /// 请求服务端在编码前旋转图像 (0, 90, 180, 270)。
    /// </summary>
    public int? Angle { get; set; }

    /// <summary>
    /// 请求服务端在编码前裁剪屏幕区域 (width:height:x:y)。
    /// </summary>
    public Rectangle? Crop { get; set; }

    /// <summary>
    /// 指定服务端要捕获的显示器ID。使用 list_displays=true 获取可用ID。
    /// </summary>
    public int? DisplayId { get; set; }

    #endregion

    #region 摄像头参数 (当 video_source=camera 时生效)

    /// <summary>
    /// 要使用的摄像头ID。
    /// </summary>
    public string? CameraId { get; set; }

    /// <summary>
    /// 期望的摄像头分辨率。
    /// </summary>
    public Size? CameraSize { get; set; }

    /// <summary>
    /// 选择摄像头朝向 (front, back, external)。
    /// </summary>
    public string? CameraFacing { get; set; }

    /// <summary>
    /// 期望的摄像头宽高比。
    /// </summary>
    public string? CameraAspectRatio { get; set; }

    /// <summary>
    /// 期望的摄像头帧率。
    /// </summary>
    public int? CameraFps { get; set; }

    /// <summary>
    /// 是否启用摄像头高速模式。
    /// </summary>
    public bool CameraHighSpeed { get; set; } = false;

    #endregion

    #region 控制与交互

    /// <summary>
    /// 请求服务端监听并处理控制事件（如点击、按键）。设置为false则为纯观看模式。
    /// </summary>
    public bool Control { get; set; } = true;

    /// <summary>
    /// 请求服务端开启“显示触摸操作”的开发者选项功能。
    /// </summary>
    public bool ShowTouches { get; set; } = false;

    /// <summary>
    /// 请求服务端持有一个Wakelock，防止设备在连接期间自动休眠。
    /// </summary>
    public bool StayAwake { get; set; } = false;

    /// <summary>
    /// 启动时，请求服务端唤醒并点亮屏幕。
    /// </summary>
    public bool PowerOn { get; set; } = true;

    /// <summary>
    /// 客户端断开连接时，请求服务端执行关闭屏幕操作。
    /// </summary>
    public bool PowerOffOnClose { get; set; } = false;

    /// <summary>
    /// 设置一个延迟（毫秒），如果客户端在这段时间内没有交互，则关闭设备屏幕。-1表示禁用。
    /// </summary>
    public int? ScreenOffTimeout { get; set; }

    /// <summary>
    /// 控制服务端是否处理剪贴板同步。
    /// </summary>
    public bool ClipboardAutosync { get; set; } = true;

    /// <summary>
    /// 请求服务端使用 HID 键盘进行输入。需要 Android 12+。
    /// </summary>
    public bool HidKeyboard { get; set; } = false;

    /// <summary>
    /// 请求服务端使用 HID 鼠标进行输入。需要 Android 12+。
    /// </summary>
    public bool HidMouse { get; set; } = false;

    /// <summary>
    /// 请求服务端在 OTG 模式下运行，以实现更直接的输入控制。
    /// </summary>
    public bool Otg { get; set; } = false;

    #endregion

    #region 连接与错误处理

    /// <summary>
    /// 告知服务端连接是隧道转发模式。
    /// </summary>
    public bool TunnelForward { get; set; } = true;

    /// <summary>
    /// 如果请求的视频设置（如分辨率）导致编码器错误，允许服务端自动尝试降低设置并重试。
    /// </summary>
    public bool DownsizeOnError { get; set; } = true;

    /// <summary>
    /// 客户端断开连接后，服务端是否应自行清理（例如删除/data/local/tmp下的server.jar）。
    /// </summary>
    public bool Cleanup { get; set; } = true;

    /// <summary>
    /// 在连接开始时发送一个哑字节以保持连接。
    /// </summary>
    public bool SendDummyByte { get; set; } = false;

    #endregion

    #region 信息获取
    // 这些参数会使服务端打印信息后立即退出，而不会进入正常的流模式。

    /// <summary>
    /// 若为true，服务端将列出可用视频/音频编码器后退出。
    /// </summary>
    public bool ListEncoders { get; set; } = false;

    /// <summary>
    /// 若为true，服务端将列出可用显示器后退出。
    /// </summary>
    public bool ListDisplays { get; set; } = false;

    /// <summary>
    /// 若为true，服务端将列出可用摄像头后退出。
    /// </summary>
    public bool ListCameras { get; set; } = false;

    /// <summary>
    /// 若为true，服务端将列出可用摄像头分辨率后退出。
    /// </summary>
    public bool ListCameraSizes { get; set; } = false;

    /// <summary>
    /// 若为true，服务端将列出可启动的应用后退出。
    /// </summary>
    public bool ListApps { get; set; } = false; // scrcpy 2.2+

    #endregion

    /// <summary>
    /// 将所有设置转换为一个空格分隔的字符串。
    /// </summary>
    public override string ToString()
    {
        var args = ToArgs();
        return string.Join(" ", args);
    }

    /// <summary>
    /// 将所有设置转换为scrcpy-server.jar可接受的参数列表。
    /// </summary>
    public List<string> ToArgs()
    {
        List<string> args =
        [
            "CLASSPATH=/data/local/tmp/scrcpy-server",
            "app_process",
            "/",
            "com.genymobile.scrcpy.Server",
            ClientVersion,
            $"log_level={LogLevel}",
        ];

        if (ListEncoders) { args.Add("list_encoders=true"); return args; }
        if (ListDisplays) { args.Add("list_displays=true"); return args; }
        if (ListCameras) { args.Add("list_cameras=true"); return args; }
        if (ListCameraSizes) { args.Add("list_camera_sizes=true"); return args; }
        if (ListApps) { args.Add("list_apps=true"); return args; }

        if (Scid.HasValue) args.Add($"scid={Scid.Value}");

        args.Add($"video={Video.ToString().ToLower()}");
        args.Add($"audio={Audio.ToString().ToLower()}");
        args.Add($"video_codec={VideoCodec}");
        args.Add($"audio_codec={AudioCodec}");
        args.Add($"video_source={VideoSource}");
        args.Add($"audio_source={AudioSource}");
        args.Add($"audio_dup={AudioDup.ToString().ToLower()}");

        if (!string.IsNullOrEmpty(VideoEncoder)) args.Add($"video_encoder={VideoEncoder}");
        if (!string.IsNullOrEmpty(AudioEncoder)) args.Add($"audio_encoder={AudioEncoder}");
        if (!string.IsNullOrEmpty(CodecOptions)) args.Add($"codec_options={CodecOptions}");

        if (MaxSize.HasValue) args.Add($"max_size={MaxSize.Value}");

        if (!string.IsNullOrEmpty(NewDisplay)) args.Add($"new_display={NewDisplay}");
        if (!VdDestroyContent) args.Add("vd_destroy_content=false");
        if (!VdSystemDecorations) args.Add("vd_system_decorations=false");

        if (VideoBitRate.HasValue) args.Add($"video_bit_rate={VideoBitRate.Value}");
        if (AudioBitRate.HasValue) args.Add($"audio_bit_rate={AudioBitRate.Value}");
        if (MaxFps.HasValue) args.Add($"max_fps={MaxFps.Value.ToString(CultureInfo.InvariantCulture)}");
        if (Angle.HasValue) args.Add($"orientation={Angle.Value}");

        args.Add($"tunnel_forward={TunnelForward.ToString().ToLower()}");

        if (Crop.HasValue)
        {
            var r = Crop.Value;
            args.Add($"crop={r.Width}:{r.Height}:{r.X}:{r.Y}");
        }

        args.Add($"control={Control.ToString().ToLower()}");

        if (DisplayId.HasValue) args.Add($"display_id={DisplayId.Value}");
        if (!string.IsNullOrEmpty(CameraId)) args.Add($"camera_id={CameraId}");
        if (CameraSize.HasValue)
        {
            var s = CameraSize.Value;
            args.Add($"camera_size={s.Width}x{s.Height}");
        }
        if (!string.IsNullOrEmpty(CameraFacing)) args.Add($"camera_facing={CameraFacing}");
        if (!string.IsNullOrEmpty(CameraAspectRatio)) args.Add($"camera_ar={CameraAspectRatio}");
        if (CameraFps.HasValue) args.Add($"camera_fps={CameraFps.Value}");
        if (CameraHighSpeed) args.Add("camera_high_speed=true");

        args.Add($"show_touches={ShowTouches.ToString().ToLower()}");
        args.Add($"stay_awake={StayAwake.ToString().ToLower()}");

        if (ScreenOffTimeout.HasValue) args.Add($"screen_off_timeout={ScreenOffTimeout.Value}");
        args.Add($"power_off_on_close={PowerOffOnClose.ToString().ToLower()}");
        args.Add($"clipboard_autosync={ClipboardAutosync.ToString().ToLower()}");
        args.Add($"downsize_on_error={DownsizeOnError.ToString().ToLower()}");
        args.Add($"cleanup={Cleanup.ToString().ToLower()}");
        args.Add($"power_on={PowerOn.ToString().ToLower()}");

        if (HidKeyboard) args.Add("hid_keyboard=true");
        if (HidMouse) args.Add("hid_mouse=true");
        if (Otg) args.Add("otg=true");
        if (SendDummyByte) args.Add("send_dummy_byte=true");

        return args;
    }
}