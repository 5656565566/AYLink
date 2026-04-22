using System;

namespace AYLink.Desktop.Services.Tasks;

public class TaskStartOptions
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public bool IsCancelable { get; init; }

    public bool IsIndeterminate { get; init; } = true;

    public Action? CancelAction { get; init; }
}
