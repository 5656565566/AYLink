using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AYLink.Desktop.Services.Tasks;

public partial class TaskItem : ObservableObject
{
    [ObservableProperty]
    public partial string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial TaskItemStatus Status { get; set; } = TaskItemStatus.Running;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsCancelable { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    public partial DateTimeOffset? FinishedAt { get; set; }

    public Action? CancelAction { get; set; }

    public bool CanCancel => IsCancelable && Status == TaskItemStatus.Running && CancelAction != null;

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CancelAction?.Invoke();
    }

}
