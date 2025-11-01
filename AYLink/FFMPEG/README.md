## 第三方组件声明

### FFmpeg-7.1.1
- **组件**: avcodec-61.dll, avutil-59.dll, swresample-5.dll, swscale-8.dll
- **来源**: [FFmpeg](https://ffmpeg.org)
- **许可证**: LGPLv3
- **状态**: 未修改的官方二进制文件
- **源码下载地址**: https://ffmpeg.org/releases/ffmpeg-7.1.1.tar.xz

推荐的编译参数 依照协议你可以自行替换 也可以使用包管理器安装
```bash
./configure --enable-shared --disable-static --disable-everything --disable-programs --disable-doc --disable-debug --enable-avcodec --enable-swscale --enable-swresample --enable-avutil --enable-decoder=h264 --enable-decoder=hevc --enable-decoder=av1 --enable-parser=h264 --enable-parser=hevc --enable-parser=av1 --enable-decoder=aac --enable-decoder=opus --enable-decoder=flac --enable-parser=aac --enable-parser=aac_latm --enable-parser=opus --enable-parser=flac
```
