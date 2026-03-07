using AYLink.Core.Models;

namespace AYLink.Desktop.Models;

public class FileSource
{
    public string Name { get; set; } = string.Empty;
    public DeviceModel? Device { get; set; }
    public string InitialPath { get; set; } = "/";
    public bool IsLocal => Device == null;
}