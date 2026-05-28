using AYLink.Core.Models;
using AYLink.Desktop.Services.Agent;

namespace AYLink.Desktop.Models;

public class FileSource
{
    public string Name { get; set; } = string.Empty;
    public DeviceModel? Device { get; set; }
    public AgentFileManager? AgentFileManager { get; set; }
    public string InitialPath { get; set; } = "/";
    public bool IsLocal => Device == null && AgentFileManager == null;
    public bool IsRemoteAgent => AgentFileManager != null;
}
