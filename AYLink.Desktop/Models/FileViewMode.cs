namespace AYLink.Desktop.Models;

public enum FileViewMode
{
    Details,
    List,
    Icons
}

public enum FileSortColumn
{
    Name,
    Type,
    Size
}

public sealed record FilePathSegment(string Name, string Path);
