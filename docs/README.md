# AYLink (安易连)

[![License](https://img.shields.io/badge/License-Apache2.0-blue.svg)](../LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg?logo=dotnet&logoColor=white)

[English](README_EN.md) | **简体中文**

**安易连（AYLink）** 是一款基于 [scrcpy](https://github.com/Genymobile/scrcpy) 核心驱动的跨平台安卓设备投屏与控制客户端。本项目使用 [Avalonia UI](https://avaloniaui.net/) 框架采用 C# 编写，致力于提供流畅、美观且支持多平台的桌面级安卓设备管理体验。

> [!TIP]
> 本项目最初为业余学习 Avalonia 框架的实践之作。无法保证绝对稳定无误。遇到任何问题或有好的建议，欢迎随时通过 Issue 提交反馈！

## ✨ 核心特性

- **跨平台支持**：完美运行于 Windows、macOS 和 Linux。
- **现代化界面**：基于 Avalonia 构建的流畅 UI，原生支持暗色模式。
- **低延迟投屏**：底层集成 scrcpy，提供原生级别的高清、低延迟投屏与操控体验。

## 📸 界面展示

| 主界面 | 功能设置 |
| :---: | :---: |
| ![主界面](screenshot/1.png) | ![功能设置](screenshot/2.png) |
| **投屏窗口** | **终端** |
| ![投屏窗口](screenshot/3.png) | ![终端](screenshot/4.png) |

## 🚀 快速开始

### 前置准备
1. 确保您的安卓设备已开启 **“开发者选项”**。
2. 在开发者选项中开启 **“USB 调试”**。
3. 使用数据线将设备连接至电脑或者使用WIFI ADB，并在手机弹窗中授权该电脑的调试权限。

### 获取与运行
前往 [releases](https://github.com/5656565566/AYLink/releases) 根据需求获取

## 🎨 高级配置：自定义背景

程序首次运行后，会在当前工作目录下自动生成一个 `bg` 文件夹。
**设置方法**：将您喜爱的图片文件放入该 `bg` 文件夹中。程序每次启动时，将自动从中随机抽取一张作为界面的背景图，打造您的专属工作台。

## 🛠️ 开发与构建

如果您是开发者，想要自行编译此项目，本项目支持针对多种操作系统和架构进行本地发布。

### 编译命令参考

| 操作系统 (OS) | 架构 (Arch) | 发布命令 |
| :--- | :--- | :--- |
| Windows | x64 | `dotnet publish -c Release -r win-x64` |
| Windows | ARM64 | `dotnet publish -c Release -r win-arm64` |
| Linux | x64 | `dotnet publish -c Release -r linux-x64` |
| Linux | ARM64 | `dotnet publish -c Release -r linux-arm64` |
| macOS | x64 (Intel) | `dotnet publish -c Release -r osx-x64` |
| macOS | ARM64 (Apple Silicon)| `dotnet publish -c Release -r osx-arm64` |

> [!TIP]
> **💡 Linux / macOS 环境提示**：
> 在 Linux 和 macOS 系统下，推荐使用系统自带的包管理器（如 `apt`, `brew` 等）安装并配置 ADB 与 FFmpeg 的环境变量，以确保程序后台能够正常调用这些依赖。

## 📦 依赖与致谢

本项目的顺利开发离不开以下优秀的开源组件：

| 项目 | 描述 |
|------|------|
| [scrcpy](https://github.com/Genymobile/scrcpy) | 提供核心的屏幕镜像与控制能力 |
| [AdvancedSharpAdbClient](https://github.com/SharpAdb/AdvancedSharpAdbClient) | 用于方便的调用 ADB |
| [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) | 提供 C# 可用的 FFmpeg 绑定 |
| [Avalonia UI](https://avaloniaui.net/) | 强大的跨平台 .NET UI 框架 |
| [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) | 提供美观的各种控件 |
| [SDL3-CS](https://github.com/ppy/SDL3-CS) | 提供 C# 可用 SDL3 绑定 |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | .NET平台高性能JSON框架 |
| [XTerm.NET](https://github.com/tomlm/XTerm.NET) | 用于解析和处理 VT100/ANSI 转义序列 |
| [Iciclecreek.Avalonia.Terminal](https://github.com/tomlm/Iciclecreek.Avalonia.Terminal) | 使用了部分源代码实现终端控件 |

## 📄 开源协议

本项目基于 [Apache-2.0 license](../LICENSE) 协议开源。
