using AdvancedSharpAdbClient;
using AYLink.Controls.Terminal;
using AYLink.Core.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 终端标签页 ViewModel - 每个设备对应一个标签页 管理 ADB shell 会话
/// </summary>
public partial class ShellTabViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private string _title = "终端";

    [ObservableProperty]
    private DeviceModel? _device;

    [ObservableProperty]
    private string _statusMessage = "未连接";

    [ObservableProperty]
    private bool _isConnected;

    private CancellationTokenSource? _sessionCts;
    private IAdbSocket? _adbSocket;
    private Stream? _shellStream;
    private bool _disposed;

    /// <summary>
    /// 引用 UI 层的 TerminalControl，由 View 层设置
    /// </summary>
    private TerminalControl? _terminalControl;

    public event Action<ShellTabViewModel>? OnCloseRequested;

    public ShellTabViewModel(DeviceModel device)
    {
        _device = device;
        Title = device.Name;
    }

    /// <summary>
    /// 绑定终端控件并启动会话
    /// </summary>
    public void AttachTerminal(TerminalControl terminal)
    {
        // 从旧控件解绑事件
        if (_terminalControl != null)
        {
            _terminalControl.UserInput -= OnUserInput;
            _terminalControl.TerminalResized -= OnTerminalResized;
        }

        _terminalControl = terminal;
        _terminalControl.UserInput += OnUserInput;
        _terminalControl.TerminalResized += OnTerminalResized;

        // 如果设备已连接 启动会话
        if (Device != null && !IsConnected)
        {
            _ = StartShellSessionAsync();
        }
    }

    /// <summary>
    /// 分离终端控件
    /// </summary>
    public void DetachTerminal()
    {
        if (_terminalControl != null)
        {
            _terminalControl.UserInput -= OnUserInput;
            _terminalControl.TerminalResized -= OnTerminalResized;
            _terminalControl = null;
        }
    }

    /// <summary>
    /// 关闭标签页
    /// </summary>
    [RelayCommand]
    private void CloseTab()
    {
        CloseShellSession();
        OnCloseRequested?.Invoke(this);
    }

    /// <summary>
    /// 启动 ADB shell 会话
    /// </summary>
    private async Task StartShellSessionAsync()
    {
        if (Device?.AdbClient == null || IsConnected) return;

        try
        {
            _adbSocket = Device.AdbClient.CreateAdbSocket();
            await _adbSocket.SetDeviceAsync(Device.DeviceData, CancellationToken.None);
            await _adbSocket.SendAdbRequestAsync("shell:", CancellationToken.None);
            await _adbSocket.ReadAdbResponseAsync(CancellationToken.None);

            IsConnected = true;
            StatusMessage = $"已连接 - {Device.Name}";
            _sessionCts = new CancellationTokenSource();
            _shellStream = _adbSocket.GetShellStream();

            WriteToTerminal($"\x1b[32m已连接到设备: {Device.Name} ({Device.Serial})\x1b[0m\r\n");

            // 读取终端输出的任务不用等待它完成 直接在后台运行
            _ = Task.Run(() => ReadShellOutputAsync(_sessionCts.Token));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellTab] StartShellSession failed: {ex}");
            WriteToTerminal($"\x1b[31m会话启动失败: {ex.Message}\x1b[0m\r\n");
            StatusMessage = $"连接失败: {ex.Message}";
            CloseShellSession();
        }
    }

    /// <summary>
    /// 终端控件尺寸变化时同步到远程 shell
    /// </summary>
    private void OnTerminalResized(object? sender, TerminalSizeEventArgs e)
    {
        
    }

    /// <summary>
    /// 持续读取 ADB shell 输出
    /// </summary>
    private async Task ReadShellOutputAsync(CancellationToken token)
    {
        if (_shellStream == null) return;

        try
        {
            var buffer = new byte[1000];
            while (!token.IsCancellationRequested && _adbSocket?.Connected == true)
            {
                int bytesRead = await _shellStream.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead > 0)
                {
                    var output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    output = output.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    WriteToTerminal(output);
                }
                else
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellTab] ReadShellOutput error: {ex}");
            WriteToTerminal($"\r\n\x1b[31m连接错误: {ex.Message}\x1b[0m\r\n");
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsConnected = false;
                StatusMessage = "已断开";
            });
            WriteToTerminal("\r\n\x1b[33m会话已断开\x1b[0m\r\n");
        }
    }

    /// <summary>
    /// 处理用户输入 转发到 ADB shell
    /// </summary>
    private async void OnUserInput(object? sender, TerminalDataEventArgs e)
    {
        if (_adbSocket == null || !IsConnected || string.IsNullOrEmpty(e.Data))
            return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(e.Data);
            await _adbSocket.SendAsync(bytes, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellTab] SendInput failed: {ex.Message}");
            WriteToTerminal($"\r\n\x1b[31m发送失败: {ex.Message}\x1b[0m\r\n");
        }
    }

    /// <summary>
    /// 写入终端控件
    /// </summary>
    private void WriteToTerminal(string data)
    {
        if (_terminalControl == null) return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            _terminalControl.WriteToTerminal(data);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _terminalControl?.WriteToTerminal(data));
        }
    }

    /// <summary>
    /// 关闭 ADB shell 会话
    /// </summary>
    private void CloseShellSession()
    {
        _sessionCts?.Cancel();

        _shellStream?.Dispose();
        _adbSocket?.Dispose();
        _sessionCts?.Dispose();

        IsConnected = false;
        _shellStream = null;
        _adbSocket = null;
        _sessionCts = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DetachTerminal();
            CloseShellSession();
        }
    }
}
