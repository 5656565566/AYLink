using AYLink.Core.Scrcpy;
using AYLink.Core.Scrcpy.Control;
using AYLink.Desktop.Services.Input;
using Avalonia;
using System;

namespace AYLink.Desktop.Services.ScreenSessions.Local;

/// <summary>
/// 提供本地投屏会话的控制消息发送与扩展控制能力
/// </summary>
internal sealed class LocalScreenControlBridge
{
    private ScrcpyClient? _client;

    public void Attach(ScrcpyClient client, IInputProcessor inputProcessor)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(inputProcessor);
        inputProcessor.SetSender(new ScrcpyClientCommandSender(client));
    }

    public void SendControl(byte[] payload)
    {
        if (_client != null && payload.Length > 0)
        {
            _client.SendControlCommand(payload);
        }
    }

    public void SendPointerMove(byte[] payload)
    {
        SendControl(payload);
    }

    public void SendStartApp(string appPackageName)
    {
        if (_client == null || string.IsNullOrWhiteSpace(appPackageName))
        {
            return;
        }

        _client.SendControlCommand(new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.StartApp,
            Data = appPackageName
        }.Serialize());
    }

    public bool SendResizeDisplayIfNeeded(
        Size newSize,
        bool isFlexDisplayEnabled,
        bool canResizeDisplay,
        bool hasReceivedFirstVideoFrame,
        Size? lastResizeRequestSize)
    {
        if (!isFlexDisplayEnabled || !canResizeDisplay || _client == null || newSize.Width <= 0 || newSize.Height <= 0)
        {
            return false;
        }

        if (!hasReceivedFirstVideoFrame)
        {
            return false;
        }

        if (lastResizeRequestSize is Size lastSize &&
            Math.Abs(lastSize.Width - newSize.Width) < 0.5 &&
            Math.Abs(lastSize.Height - newSize.Height) < 0.5)
        {
            return false;
        }

        _client.SendControlCommand(new ControlMsgModel.ControlMsg
        {
            Type = ControlMsgModel.ControlMsgType.ResizeDisplay,
            Data = new ControlMsgModel.ResizeDisplayData
            {
                Width = (ushort)Math.Max(1, newSize.Width),
                Height = (ushort)Math.Max(1, newSize.Height)
            }
        }.Serialize());

        return true;
    }

    public void Detach()
    {
        _client = null;
    }

    private sealed class ScrcpyClientCommandSender(ScrcpyClient client) : IControlCommandSender
    {
        private readonly ScrcpyClient _client = client;

        public void SendCommand(byte[] controlMessage)
        {
            _client.SendControlCommand(controlMessage);
        }
    }
}
