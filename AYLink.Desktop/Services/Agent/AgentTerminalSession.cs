using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Agent;
using Newtonsoft.Json.Linq;

namespace AYLink.Desktop.Services.Agent;

/// <summary>
/// 远程 Agent 终端会话
/// 负责通过 WebSocket 桥接设备 shell 输入输出
/// </summary>
public sealed class AgentTerminalSession(AgentServerRuntime runtime, int remoteDeviceId) : IAsyncDisposable
{
    private readonly AgentServerRuntime _runtime = runtime;
    private readonly int _remoteDeviceId = remoteDeviceId;
    private readonly ClientWebSocket _socket = new();
    private CancellationTokenSource? _cts;
    private Task? _receiveLoopTask;
    private bool _started;

    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;
    public event Action? Closed;
    public event Action? Ready;

    /// <summary>
    /// 启动终端会话
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        await _socket.ConnectAsync(_runtime.Client.BuildTerminalWebSocketUri(_remoteDeviceId), cancellationToken);
        _runtime.TouchSuccess();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        _started = true;
    }

    /// <summary>
    /// 向终端写入输入
    /// </summary>
    public Task SendInputAsync(string data, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(new JObject
        {
            ["type"] = "input",
            ["data"] = data
        }, cancellationToken);
    }

    /// <summary>
    /// 调整终端大小
    /// </summary>
    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(new JObject
        {
            ["type"] = "resize",
            ["cols"] = cols,
            ["rows"] = rows
        }, cancellationToken);
    }

    /// <summary>
    /// 关闭终端会话
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "terminal closed", CancellationToken.None);
            }
            catch
            {
            }
        }

        if (_receiveLoopTask != null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
        _socket.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var payload = await ReceiveTextMessageAsync(cancellationToken);
                if (payload == null)
                {
                    break;
                }

                var message = JObject.Parse(payload);
                var type = message.Value<string>("type");
                switch (type)
                {
                    case "ready":
                        Ready?.Invoke();
                        break;
                    case "output":
                        OutputReceived?.Invoke(message.Value<string>("data") ?? string.Empty);
                        break;
                    case "error":
                        ErrorReceived?.Invoke(message.Value<string>("message") ?? "终端会话已断开");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke(ex.Message);
        }
        finally
        {
            Closed?.Invoke();
        }
    }

    private async Task<string?> ReceiveTextMessageAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        await using var stream = new MemoryStream();

        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task SendJsonAsync(JObject payload, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
        await _socket.SendAsync(bytes.AsMemory(0, bytes.Length), WebSocketMessageType.Text, true, cancellationToken);
    }
}
