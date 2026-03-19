using System.Collections;
using System.ComponentModel;

namespace AYLink.Desktop.ViewModels;

public interface ITabbedPageViewModel : INotifyPropertyChanged
{
    IEnumerable Tabs { get; }
    object? SelectedTab { get; set; }
    string EmptyStateIcon { get; }
    string EmptyStateTitle { get; }
    string EmptyStateDescription { get; }

    bool DetachTab(TabItemViewModelBase tab);
    bool AttachTab(TabItemViewModelBase tab, int index = -1);
}
