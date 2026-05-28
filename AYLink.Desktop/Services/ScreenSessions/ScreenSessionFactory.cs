using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Core.Scrcpy;
using AYLink.Core.Utils;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Audio;
using AYLink.Desktop.Services.Input;
using AYLink.Desktop.Services.ScreenSessions.Agent;
using AYLink.Desktop.Services.ScreenSessions.Local;

namespace AYLink.Desktop.Services.ScreenSessions;

/// <summary>
/// 为投屏页创建具体的会话实现与输入处理器
/// </summary>
internal sealed class ScreenSessionFactory : IScreenSessionFactory
{
    private readonly AudioPlayer _audioPlayer;

    public ScreenSessionFactory(AudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer;
    }

    public IScreenSession CreateLocalSession(DeviceModel device, string? appPackageName, string? appDisplayName)
    {
        _ = appDisplayName;
        return new LocalScreenSession(device, appPackageName, _audioPlayer);
    }

    public IScreenSession CreateRemoteSession(DeviceDescriptor remoteDevice, AgentServerRuntime remoteServer, string? appPackageName, string? appDisplayName)
    {
        return new AgentScreenSession(remoteDevice, remoteServer, appPackageName, appDisplayName, _audioPlayer);
    }

    public IInputProcessor CreateInputProcessor(DeviceModel? device)
    {
        if (device == null)
        {
            return new DefaultInputProcessor();
        }

        var deviceConfig = ConfigManager.Instance.LoadConfig<DeviceConfig>(
            HashHelper.ToMd5Hash(device.Serial));
        device.ServerOptions ??= new ServerOptions();
        deviceConfig.ApplyConfig(device.ServerOptions);

        if (device.ServerOptions.HidKeyboard || device.ServerOptions.HidMouse)
        {
            return new HidInputProcessor(device.ServerOptions.HidKeyboard, device.ServerOptions.HidMouse);
        }

        return new DefaultInputProcessor();
    }
}
