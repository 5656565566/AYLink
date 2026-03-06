namespace AYLink.Core.Models;

public class FileSystemModel(string name, int size , bool isDirectory = false)
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = name;
    /// <summary>
    /// 是否是路径
    /// </summary>
    public bool IsDirectory { get; set; } = isDirectory;
    /// <summary>
    /// 文件大小
    /// </summary>
    public int Size { get; set; } = size;
}
