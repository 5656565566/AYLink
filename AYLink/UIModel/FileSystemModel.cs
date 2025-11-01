using FluentAvalonia.UI.Controls;

namespace AYLink.UIModel;

internal class FileSystemModel(string name, int size , bool isDirectory = false)
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = name;
    /// <summary>
    /// 是路径？
    /// </summary>
    public bool IsDirectory { get; set; } = isDirectory;
    /// <summary>
    /// 文件大小
    /// </summary>
    public int Size { get; set; } = size;
    /// <summary>
    /// 图标 TODO 更多文件类型图标支持
    /// </summary>
    public Symbol Icon => IsDirectory ? Symbol.Folder : Symbol.Document;
}
