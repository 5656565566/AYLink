using AdvancedSharpAdbClient;
using AYLink.Controls.Terminal;
using AYLink.Core.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class ShellTabViewModel : TabItemViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool _isConnected;

    private CancellationTokenSource? _sessionCts;
    private IAdbSocket? _adbSocket;
    private Stream? _shellStream;
    private bool _disposed;
    private TerminalControl? _terminalControl;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ShellTabViewModel(DeviceModel device)
    {
        Device = device;
        Title = device.Name;
        StatusMessage = Services.Localization.LocalizationManager.Instance.GetString("ShellTab.NotConnected", "未连接");
    }

    public void AttachTerminal(TerminalControl terminal)
    {
        if (_terminalControl != null)
        {
            _terminalControl.UserInput -= OnUserInput;
            _terminalControl.TerminalResized -= OnTerminalResized;
        }

        _terminalControl = terminal;
        _terminalControl.UserInput += OnUserInput;
        _terminalControl.TerminalResized += OnTerminalResized;

        if (Device != null && !IsConnected)
        {
            _ = StartShellSessionAsync();
        }
    }

    public void DetachTerminal()
    {
        if (_terminalControl != null)
        {
            _terminalControl.UserInput -= OnUserInput;
            _terminalControl.TerminalResized -= OnTerminalResized;
            _terminalControl = null;
        }
    }

    protected override void CloseTab()
    {
        CloseShellSession();
        base.CloseTab();
    }

    private async Task StartShellSessionAsync()
    {
        if (Device?.AdbClient == null || IsConnected) return;

        try
        {
            _adbSocket = Device.AdbClient.CreateAdbSocket();
            await _adbSocket.SetDeviceAsync(Device.DeviceData, CancellationToken.None);

            await _adbSocket.SendAdbRequestAsync("shell,v2,pty,TERM=xterm-256color:", CancellationToken.None);

            await _adbSocket.ReadAdbResponseAsync(CancellationToken.None);

            IsConnected = true;
            var localizer = Services.Localization.LocalizationManager.Instance;
            StatusMessage = string.Format(localizer.GetString("ShellTab.ConnectedStatus", "已连接 - {0}"), Device.Name);
            _sessionCts = new CancellationTokenSource();
            _shellStream = GetRawNetworkStream(_adbSocket);

            WriteToTerminal($"\x1b[32m{string.Format(localizer.GetString("ShellTab.ConnectedMessage", "已连接到设备: {0} ({1})"), Device.Name, Device.Serial)}\x1b[0m\r\n");

            if (_terminalControl != null)
            {
                var cols = Math.Max(1, _terminalControl.Terminal.Cols);
                var rows = Math.Max(1, _terminalControl.Terminal.Rows);
                string sizeStr = $"{rows}x{cols},0x0";
                byte[] sizeBytes = Encoding.UTF8.GetBytes(sizeStr);
                byte[] packet = new byte[5 + sizeBytes.Length];
                packet[0] = 5;
                byte[] lenBytes = BitConverter.GetBytes(sizeBytes.Length);
                Array.Copy(lenBytes, 0, packet, 1, 4);
                Array.Copy(sizeBytes, 0, packet, 5, sizeBytes.Length);

                await SendPacketSafeAsync(packet, "Initial Resize");
            }

            _ = Task.Run(() => ReadShellOutputAsync(_sessionCts.Token));
        }
        catch (Exception ex)
        {
            var localizer = Services.Localization.LocalizationManager.Instance;
            WriteToTerminal($"\x1b[31m{string.Format(localizer.GetString("ShellTab.SessionStartFailed", "会话启动失败: {0}"), ex.Message)}\x1b[0m\r\n");
            StatusMessage = string.Format(localizer.GetString("ShellTab.ConnectionFailed", "连接失败: {0}"), ex.Message);
            CloseShellSession();
        }
    }

    private async void OnTerminalResized(object? sender, TerminalSizeEventArgs e)
    {
        if (_shellStream == null || !IsConnected) return;
        try
        {
            string sizeStr = $"{e.Rows}x{e.Cols},0x0";
            byte[] sizeBytes = Encoding.UTF8.GetBytes(sizeStr);
            byte[] packet = new byte[5 + sizeBytes.Length];
            packet[0] = 5;
            byte[] lenBytes = BitConverter.GetBytes(sizeBytes.Length);
            Array.Copy(lenBytes, 0, packet, 1, 4);
            Array.Copy(sizeBytes, 0, packet, 5, sizeBytes.Length);

            await SendPacketSafeAsync(packet, "Resize");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellTab] Resize 异常: {ex.Message}");
        }
    }

    private async Task ReadShellOutputAsync(CancellationToken token)
    {
        if (_shellStream == null) return;

        try
        {
            byte[] header = new byte[5];
            while (!token.IsCancellationRequested && _adbSocket?.Connected == true)
            {
                int headerRead = await ReadExactAsync(_shellStream, header, 5, token);
                if (headerRead < 5)
                {
                    break;
                }

                byte id = header[0];
                int length = header[1] | (header[2] << 8) | (header[3] << 16) | (header[4] << 24);


                if (length > 0)
                {
                    // 防止错位时申请爆炸内存引发 OOM
                    if (length > 10 * 1024 * 1024)
                    {
                        break;
                    }

                    byte[] payload = new byte[length];
                    int payloadRead = await ReadExactAsync(_shellStream, payload, length, token);

                    if (payloadRead < length) break;

                    if (id == 1 || id == 2)
                    {
                        string output = Encoding.UTF8.GetString(payload);
                        output = output.Replace("\r\n", "\n").Replace("\n", "\r\n");
                        WriteToTerminal(output);
                    }
                }

                if (id == 3)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellTab-Read] ReadShellOutput 异常: {ex}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsConnected = false;
                StatusMessage = Services.Localization.LocalizationManager.Instance.GetString("ShellTab.DisconnectedStatus", "已断开");
            });
            WriteToTerminal($"\r\n\x1b[33m{Services.Localization.LocalizationManager.Instance.GetString("ShellTab.SessionDisconnected", "会话已断开")}\x1b[0m\r\n");
        }
    }

    private async static Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken token)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), token);
            if (read == 0) return totalRead;
            totalRead += read;
        }
        return totalRead;
    }

    private async void OnUserInput(object? sender, TerminalDataEventArgs e)
    {
        if (_shellStream == null || !IsConnected || string.IsNullOrEmpty(e.Data)) return;

        try
        {
            byte[] textBytes = Encoding.UTF8.GetBytes(e.Data);
            byte[] packet = new byte[5 + textBytes.Length];

            packet[0] = 0; // ID: STDIN
            byte[] lenBytes = BitConverter.GetBytes(textBytes.Length);
            Array.Copy(lenBytes, 0, packet, 1, 4);
            Array.Copy(textBytes, 0, packet, 5, textBytes.Length);

            string debugText = e.Data.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\x03", "^C");

            await SendPacketSafeAsync(packet, "UserInput");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellTab-Write] SendInput 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 统一的安全发送方法 直接写底层流
    /// </summary>
    /// <param name="packet">数据包</param>
    /// <returns></returns>
    private async Task SendPacketSafeAsync(byte[] packet, string _)
    {
        if (_shellStream == null) return;

        // 5秒超时防止死锁卡UI
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            return;
        }

        try
        {
            await _shellStream.WriteAsync(packet, CancellationToken.None);
            await _shellStream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }
    /// <summary>
    /// 保持UI线程写入到终端控件
    /// </summary>
    /// <param name="data"></param>
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
        GC.SuppressFinalize(this);
        if (!_disposed)
        {
            _disposed = true;
            DetachTerminal();
            CloseShellSession();
        }
    }

    /// <summary>
    /// 使用反射绕过 AdbClient 的魔改 Stream 直接提取最底层的纯净 TCP 网络流
    /// 彻底解决 v2 协议下 CRLF 替换导致的二进制包错位（粘包/断层）问题
    /// </summary>
    /// <param name="adbSocket"></param>
    /// <returns></returns>
    private static Stream GetRawNetworkStream(IAdbSocket adbSocket)
    {
        try
        {
            // BindingFlags 涵盖所有公开、私有、实例的字段和方法
            var flags = System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public;

            var socketType = adbSocket.GetType();

            // 暴力扫描所有私有/公开字段 寻找内部隐藏的 tcpSocket
            foreach (var field in socketType.GetFields(flags))
            {
                var fieldValue = field.GetValue(adbSocket);
                if (fieldValue == null) continue;

                var fieldType = fieldValue.GetType();

                // AdvancedSharpAdbClient 通常将底层连接放在名为 tcpSocket 的 ITcpSocket 字段中
                if (fieldType.Name.Contains("TcpSocket") || fieldType.GetInterface("ITcpSocket") != null)
                {
                    // 找到了内部的 TCP 对象，尝试调用它的 GetStream() 方法
                    var getStreamMethod = fieldType.GetMethod("GetStream", flags);
                    if (getStreamMethod != null)
                    {
                        var stream = getStreamMethod.Invoke(fieldValue, null) as Stream;
                        if (stream != null)
                        {
                            Debug.WriteLine($"[ShellTab] 成功从内部私有字段 '{field.Name}' 提取底层纯净流！");
                            return stream;
                        }
                    }

                    // 如果没有 GetStream()，尝试直接获取 Socket 属性
                    var socketProp = fieldType.GetProperty("Socket", flags);
                    if (socketProp != null)
                    {
                        var rawSocket = socketProp.GetValue(fieldValue) as System.Net.Sockets.Socket;
                        if (rawSocket != null)
                        {
                            Debug.WriteLine($"[ShellTab] 成功从内部私有字段 '{field.Name}.Socket' 提取底层 Socket！");
                            return new System.Net.Sockets.NetworkStream(rawSocket, false);
                        }
                    }
                }

                // 万一它直接是个 Socket
                if (fieldValue is System.Net.Sockets.Socket s)
                {
                    Debug.WriteLine($"[ShellTab] 成功从字段 '{field.Name}' 提取底层 Socket！");
                    return new System.Net.Sockets.NetworkStream(s, false);
                }
            }

            // 作为保底 调用自带的 GetStream()
            var directGetStream = socketType.GetMethod("GetStream", flags);
            if (directGetStream != null)
            {
                var stream = directGetStream.Invoke(adbSocket, null) as Stream;
                if (stream != null && !stream.GetType().Name.Contains("ShellStream"))
                {
                    Debug.WriteLine("[ShellTab] 成功直接通过 GetStream() 提取底层流！");
                    return stream;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellTab] 暴力提取纯净流异常: {ex}");
        }

        return adbSocket.GetShellStream();
    }
}