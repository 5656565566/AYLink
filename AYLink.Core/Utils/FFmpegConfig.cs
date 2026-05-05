using FFmpeg.AutoGen;

namespace AYLink.Core.Utils;

public static class FFmpegConfig
{
    /// <summary>
    /// 设置 FFmpeg 二进制文件的根路径
    /// </summary>
    /// <param name="path">FFmpeg 库所在的目录路径</param>
    public static void SetRootPath(string path)
    {
        ffmpeg.RootPath = path;
    }
}
