using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Devices;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Devices;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.ViewModels.Pages;

/// <summary>
/// 设备分组管理页面 ViewModel
/// 负责管理本地设备分组定义以及本地设备的分组归属
/// </summary>
public partial class DeviceGroupsPageViewModel : PageViewModelBase
{
    public override string PageKey => "DeviceGroups";
    public override string Title => LocalizationManager.Instance.GetString("DeviceGroupsPage.Title", "设备分组");

    private readonly DeviceCatalogService _deviceCatalog = DeviceCatalogService.Instance;

    /// <summary>
    /// 本地设备分组列表
    /// </summary>
    public ObservableCollection<DeviceGroupItemViewModel> Groups { get; } = [];

    /// <summary>
    /// 当前编辑分组下可选择的本地设备列表
    /// </summary>
    public ObservableCollection<DeviceGroupDeviceItemViewModel> Devices { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsListExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveEditor))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingSystemGroup))]
    [NotifyPropertyChangedFor(nameof(CanEditGroupForm))]
    [NotifyPropertyChangedFor(nameof(CanSaveEditor))]
    public partial DeviceGroupItemViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial bool IsEditorOpen { get; set; }

    [ObservableProperty]
    public partial bool IsCreateMode { get; set; }

    [ObservableProperty]
    public partial string EditingName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditingDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedDeviceCount { get; set; }

    [ObservableProperty]
    public partial int FilteredGroupCount { get; set; }

    /// <summary>
    /// 当前是否正在编辑系统内置分组
    /// </summary>
    public bool IsEditingSystemGroup => SelectedGroup?.IsSystem == true;

    /// <summary>
    /// 当前是否允许编辑分组基础信息和设备归属
    /// </summary>
    public bool CanEditGroupForm => !IsEditingSystemGroup;

    /// <summary>
    /// 当前是否允许保存分组编辑内容
    /// </summary>
    public bool CanSaveEditor => !IsSaving && CanEditGroupForm;

    public DeviceGroupsPageViewModel()
    {
        _ = ReloadAsync();
    }

    public override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);
        _ = ReloadAsync();
    }

    partial void OnSearchKeywordChanged(string value)
    {
        ApplyGroupFilter();
    }

    [RelayCommand]
    private static void BackToSettings()
    {
        NavigationService.Instance.NavigateTo("Settings");
    }

    [RelayCommand]
    private void BackToList()
    {
        CloseEditor();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadAsync();
    }

    [RelayCommand]
    private void CreateGroup()
    {
        OpenEditor(null, true);
    }

    private void ManageGroup(DeviceGroupItemViewModel group)
    {
        OpenEditor(group, false);
    }

    private async Task DeleteGroupAsync(DeviceGroupItemViewModel item)
    {
        var localizer = LocalizationManager.Instance;
        var result = await DialogService.ShowMessageAsync(
            localizer.GetString("DeviceGroupsPage.DeleteGroup", "删除分组"),
            string.Format(localizer.GetString("DeviceGroupsPage.DeleteGroupConfirm", "确定删除分组 {0} 吗？"), item.Name),
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (!_deviceCatalog.DeleteLocalDeviceGroup(item.Id))
        {
            NotificationService.Instance.ShowWarning(
                localizer.GetString("Dialog.Warning", "警告"),
                localizer.GetString("DeviceGroupsPage.DeleteFailed", "分组删除失败"));
            return;
        }

        CloseEditor();
        await ReloadAsync();
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditorOpen = false;
        IsCreateMode = false;
        SelectedGroup = null;
        EditingName = string.Empty;
        EditingDescription = string.Empty;
        Devices.Clear();
        SelectedDeviceCount = 0;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveEditor()
    {
        var localizer = LocalizationManager.Instance;
        var name = EditingName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = localizer.GetString("DeviceGroupsPage.GroupNameRequired", "请输入分组名称");
            return;
        }

        IsSaving = true;
        StatusMessage = string.Empty;

        try
        {
            DeviceGroupDescriptor? group;
            if (IsCreateMode)
            {
                group = _deviceCatalog.CreateLocalDeviceGroup(name, EditingDescription);
            }
            else if (SelectedGroup != null)
            {
                group = _deviceCatalog.UpdateLocalDeviceGroup(SelectedGroup.Id, name, EditingDescription);
            }
            else
            {
                group = null;
            }

            if (group == null)
            {
                StatusMessage = localizer.GetString("DeviceGroupsPage.SaveFailed", "分组保存失败");
                return;
            }

            if (!group.IsSystem)
            {
                SaveDeviceAssignments(group.Id);
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                IsCreateMode
                    ? localizer.GetString("DeviceGroupsPage.GroupCreateSuccess", "设备分组已创建")
                    : localizer.GetString("DeviceGroupsPage.GroupSaveSuccess", "设备分组已更新"));

            CloseEditor();
            await ReloadAsync();
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ReloadAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var devices = await _deviceCatalog.RefreshAllAsync(CancellationToken.None);
            var localDevices = devices
                .Where(item => item.SourceKind == DeviceSourceKind.Local)
                .OrderBy(item => item.Name)
                .ThenBy(item => item.Serial)
                .ToList();

            Groups.Clear();
            foreach (var group in _deviceCatalog.GetLocalDeviceGroups())
            {
                var count = localDevices.Count(device => DeviceBelongsToGroup(device, group));
                Groups.Add(new DeviceGroupItemViewModel(group, count, ManageGroup, DeleteGroupAsync));
            }

            ApplyGroupFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OpenEditor(DeviceGroupItemViewModel? group, bool createMode)
    {
        IsCreateMode = createMode;
        SelectedGroup = group;
        EditingName = group?.Name ?? string.Empty;
        EditingDescription = group?.Description ?? string.Empty;
        StatusMessage = string.Empty;

        Devices.Clear();
        var allDevices = await _deviceCatalog.RefreshAllAsync(CancellationToken.None);
        foreach (var device in allDevices
            .Where(item => item.SourceKind == DeviceSourceKind.Local)
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Serial))
        {
            var isSelected = !createMode && group != null && DeviceBelongsToGroup(device, group);
            var item = new DeviceGroupDeviceItemViewModel(device, isSelected);
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DeviceGroupDeviceItemViewModel.IsSelected))
                {
                    UpdateSelectedDeviceCount();
                }
            };
            Devices.Add(item);
        }

        UpdateSelectedDeviceCount();
        IsEditorOpen = true;
    }

    private void SaveDeviceAssignments(string groupId)
    {
        foreach (var device in Devices)
        {
            var groupIds = device.OriginalGroupIds
                .Where(id => !string.Equals(id, groupId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (device.IsSelected)
            {
                groupIds.Add(groupId);
            }

            _deviceCatalog.SetLocalDeviceGroups(device.Serial, groupIds);
        }
    }

    private void ApplyGroupFilter()
    {
        var keyword = SearchKeyword.Trim();
        var filteredCount = 0;
        foreach (var group in Groups)
        {
            group.IsVisible = string.IsNullOrWhiteSpace(keyword) ||
                group.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                group.Description.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

            if (group.IsVisible)
            {
                filteredCount++;
            }
        }

        FilteredGroupCount = filteredCount;
    }

    private void UpdateSelectedDeviceCount()
    {
        SelectedDeviceCount = Devices.Count(item => item.IsSelected);
    }

    private static bool DeviceBelongsToGroup(DeviceDescriptor device, DeviceGroupDescriptor group)
    {
        return device.Groups.Any(item => string.Equals(item.Id, group.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool DeviceBelongsToGroup(DeviceDescriptor device, DeviceGroupItemViewModel group)
    {
        return device.Groups.Any(item => string.Equals(item.Id, group.Id, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 设备分组列表项 ViewModel
/// </summary>
public partial class DeviceGroupItemViewModel(
    DeviceGroupDescriptor group,
    int deviceCount,
    Action<DeviceGroupItemViewModel> manageAction,
    Func<DeviceGroupItemViewModel, Task> deleteAction) : ViewModelBase
{
    private readonly Action<DeviceGroupItemViewModel> _manageAction = manageAction;
    private readonly Func<DeviceGroupItemViewModel, Task> _deleteAction = deleteAction;

    public string Id { get; } = group.Id;

    public string Name { get; } = group.Name;

    public string Description { get; } = group.Description;

    public int SortOrder { get; } = group.SortOrder;

    public bool IsSystem { get; } = group.IsSystem;

    public int DeviceCount { get; } = deviceCount;

    public bool CanDelete => !IsSystem;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [RelayCommand]
    private void Manage()
    {
        _manageAction(this);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task Delete()
        => _deleteAction(this);
}

/// <summary>
/// 分组编辑中的本地设备选择项
/// </summary>
public partial class DeviceGroupDeviceItemViewModel(DeviceDescriptor device, bool isSelected) : ViewModelBase
{
    public string Name { get; } = device.Name;

    public string Serial { get; } = device.Serial;

    public string ConnectionType { get; } = device.ConnectionType;

    public IReadOnlyList<string> OriginalGroupIds { get; } = device.Groups.Select(item => item.Id).ToList();

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}
