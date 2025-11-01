# AYLink
## 安易连 跨平台安卓投屏控制应用

[English](README_EN.md)

安易连（AYLink）是一个使用 [scrcpy](https://github.com/Genymobile/scrcpy) 对安卓设备进行投屏和控制的跨平台 C# 开源程序。项目使用 [Avalonia UI](https://avaloniaui.net/) 完成了跨平台用户界面的开发。

> [!NOTE]
> ### 早期阶段提醒
> 该项目目前处于**早期开发阶段** 仅为业余学习 **Avalonia** 框架时开发 **不能保障稳定无误**。
> 遇到任何问题或有任何建议 欢迎随时反馈！

### 平台支持与发布命令

本项目可以针对多种操作系统和架构进行编译和发布

| 操作系统 (OS) | 架构 (Architecture) | 发布命令 |
| :--- | :--- | :--- |
| Windows | x64 | `dotnet publish -c Release -r win-x64` |
| Windows | ARM64 | `dotnet publish -c Release -r win-arm64` |
| Linux | x64 | `dotnet publish -c Release -r linux-x64` |
| Linux | ARM64 | `dotnet publish -c Release -r linux-arm64` |
| macOS | x64 (Intel) | `dotnet publish -c Release -r osx-x64` |
| macOS | ARM64 (Apple Silicon) | `dotnet publish -c Release -r osx-arm64` |

当使用 Linux macOS 的时候 你可以使用包管理器和环境变量配置ADB / FFmpeg

### UI 界面展示（暗色模式）

![alt text](img/1.png)
![alt text](img/2.png)
![alt text](img/3.png)

### 自定义背景说明

程序运行后，会在当前目录下生成一个 `bg` 文件夹

**使用方法：** 将您喜欢的图片文件放入 `bg` 文件夹中，程序将自动从中随机选择一张作为程序的背景图。通过此方式即可自定义程序背景

### 第三方组件与依赖

本项目依赖以下核心第三方组件：

| 组件 | 说明和参考 |
| :--- | :--- |
| **ADB** | [AYLink/ADB/README.md](AYLink/ADB/README.md) |
| **FFmpeg** | [AYLink/FFmpeg/README.md](AYLink/FFmpeg/README.md) |
| **Scrcpy** | [AYLink/Scrcpy/README.md](AYLink/Scrcpy/README.md) |

其他详细的项目依赖，请参考 [AYLink/AYLink.csproj](AYLink/AYLink.csproj) 文件