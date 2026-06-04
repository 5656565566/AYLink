using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Agent;
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
/// 远程设备分组管理页面 ViewModel
/// 负责管理指定远程 Agent 服务器下的设备分组、角色授权和用户授权
/// </summary>
public partial class ServerRemoteDeviceGroupsPageViewModel : PageViewModelBase<ServerRemoteDeviceGroupsNavigationArgs>
{
    public override string PageKey => "ServerRemoteDeviceGroups";
    public override string Title
    {
        get
        {
            var suffix = LocalizationManager.Instance.GetString("ServerRemoteDeviceGroupsPage.Title", "远程设备分组");
            return string.IsNullOrWhiteSpace(ServerName) ? suffix : $"{ServerName} - {suffix}";
        }
    }

    private readonly DeviceCatalogService _deviceCatalog = DeviceCatalogService.Instance;
    private readonly AgentSessionService _agentSessions = AgentSessionService.Instance;
    private string _serverId = string.Empty;
    private List<DeviceDescriptor> _remoteDevices = [];
    private List<AgentAccountUserDto> _managedUsers = [];
    private List<AgentRoleDto> _managedRoles = [];

    public ObservableCollection<RemoteDeviceGroupCardViewModel> Groups { get; } = [];
    public ObservableCollection<RemoteGroupDeviceSelectionItemViewModel> Devices { get; } = [];
    public ObservableCollection<RemoteGroupRoleSelectionItemViewModel> Roles { get; } = [];
    public ObservableCollection<RemoteGroupUserSelectionItemViewModel> Users { get; } = [];

    [ObservableProperty]
    public partial string ServerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanManageAccounts { get; set; }

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeviceSearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RoleSearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UserSearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVisibleGroups))]
    [NotifyPropertyChangedFor(nameof(HasEmptyState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveEditor))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingSystemGroup))]
    [NotifyPropertyChangedFor(nameof(CanEditGroupForm))]
    [NotifyPropertyChangedFor(nameof(GroupEditorTitle))]
    public partial RemoteDeviceGroupCardViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupEditorTitle))]
    public partial bool IsEditorOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupEditorTitle))]
    public partial bool IsCreateMode { get; set; }

    [ObservableProperty]
    public partial string EditingName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditingDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedDeviceCount { get; set; }

    [ObservableProperty]
    public partial int SelectedRoleCount { get; set; }

    [ObservableProperty]
    public partial int SelectedUserCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVisibleGroups))]
    public partial int FilteredGroupCount { get; set; }

    public bool IsEditingSystemGroup => SelectedGroup?.IsSystem == true;
    public bool CanEditGroupForm => !IsEditingSystemGroup;
    public bool CanSaveEditor => !IsSaving;
    public bool HasVisibleGroups => FilteredGroupCount > 0;
    public bool HasEmptyState => !IsLoading && !HasVisibleGroups;
    public string GroupEditorTitle
    {
        get
        {
            var localizer = LocalizationManager.Instance;
            if (IsCreateMode)
            {
                return localizer.GetString("ServerRemoteDeviceGroupsPage.CreateGroup", "新建设备分组");
            }

            if (IsEditingSystemGroup)
            {
                return localizer.GetString("ServerRemoteDeviceGroupsPage.ManageAuthorization", "管理授权");
            }

            return localizer.GetString("ServerRemoteDeviceGroupsPage.EditGroup", "编辑设备分组");
        }
    }

    protected override void OnNavigatedTo(ServerRemoteDeviceGroupsNavigationArgs args)
    {
        _serverId = args.ServerId;
        ServerName = args.ServerName;
        CanManageAccounts = args.CanManageAccounts;
        OnPropertyChanged(nameof(Title));
        _ = ReloadAsync();
    }

    partial void OnSearchKeywordChanged(string value)
    {
        ApplyGroupFilter();
    }

    partial void OnDeviceSearchKeywordChanged(string value)
    {
        ApplyDeviceFilter();
    }

    partial void OnRoleSearchKeywordChanged(string value)
    {
        ApplyRoleFilter();
    }

    partial void OnUserSearchKeywordChanged(string value)
    {
        ApplyUserFilter();
    }

    [RelayCommand]
    private static void BackToServerSettings()
    {
        NavigationService.Instance.GoBack();
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
    private Task CreateGroup()
    {
        return OpenEditorAsync(null, true);
    }

    private Task ManageGroupAsync(RemoteDeviceGroupCardViewModel group)
    {
        return OpenEditorAsync(group, false);
    }

    private async Task DeleteGroupAsync(RemoteDeviceGroupCardViewModel item)
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

        if (!await _deviceCatalog.DeleteRemoteDeviceGroupAsync(_serverId, item.Id))
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
        DeviceSearchKeyword = string.Empty;
        RoleSearchKeyword = string.Empty;
        UserSearchKeyword = string.Empty;
        Devices.Clear();
        Roles.Clear();
        Users.Clear();
        SelectedDeviceCount = 0;
        SelectedRoleCount = 0;
        SelectedUserCount = 0;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveEditor()
    {
        var localizer = LocalizationManager.Instance;
        var name = EditingName.Trim();
        if (!IsEditingSystemGroup && string.IsNullOrWhiteSpace(name))
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
                group = await _deviceCatalog.CreateRemoteDeviceGroupAsync(_serverId, name, EditingDescription.Trim(), CancellationToken.None);
            }
            else if (SelectedGroup != null)
            {
                group = IsEditingSystemGroup
                    ? SelectedGroup.ToDescriptor()
                    : await _deviceCatalog.UpdateRemoteDeviceGroupAsync(_serverId, SelectedGroup.Id, name, EditingDescription.Trim(), CancellationToken.None);
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
                await SaveDeviceAssignmentsAsync(group.Id);
            }

            if (CanManageAccounts)
            {
                await SaveRoleAssignmentsAsync(group.Id);
                await SaveUserAssignmentsAsync(group.Id);
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                IsCreateMode
                    ? localizer.GetString("DeviceGroupsPage.GroupCreateSuccess", "设备分组已创建")
                    : localizer.GetString("DeviceGroupsPage.GroupSaveSuccess", "设备分组已更新"));

            CloseEditor();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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
            var groupsTask = _deviceCatalog.GetRemoteDeviceGroupsAsync(_serverId, null, CancellationToken.None);
            var devicesTask = _deviceCatalog.RefreshAllAsync(CancellationToken.None);
            var accountsTask = CanManageAccounts
                ? _agentSessions.GetAccountDataAsync(_serverId, CancellationToken.None)
                : Task.FromResult<AgentAccountDataResponse?>(null);

            await Task.WhenAll(groupsTask, devicesTask, accountsTask);

            var groups = groupsTask.Result
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _remoteDevices = devicesTask.Result
                .Where(item => item.SourceKind == DeviceSourceKind.Agent && string.Equals(item.ProviderId, _serverId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name)
                .ThenBy(item => item.Serial)
                .ToList();

            var accountData = accountsTask.Result;
            _managedUsers = accountData?.Users?
                .OrderBy(item => item.Username, StringComparer.CurrentCultureIgnoreCase)
                .ToList() ?? [];
            _managedRoles = accountData?.Roles?
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList() ?? [];

            Groups.Clear();
            foreach (var group in groups)
            {
                var deviceCount = group.DeviceCount > 0 || group.IsSystem
                    ? group.DeviceCount
                    : _remoteDevices.Count(device => DeviceBelongsToGroup(device, group.Id));
                Groups.Add(new RemoteDeviceGroupCardViewModel(group, deviceCount, ManageGroupAsync, DeleteGroupAsync));
            }

            ApplyGroupFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Groups.Clear();
            FilteredGroupCount = 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenEditorAsync(RemoteDeviceGroupCardViewModel? group, bool createMode)
    {
        IsCreateMode = createMode;
        SelectedGroup = group;
        EditingName = group?.Name ?? string.Empty;
        EditingDescription = group?.Description ?? string.Empty;
        DeviceSearchKeyword = string.Empty;
        RoleSearchKeyword = string.Empty;
        UserSearchKeyword = string.Empty;
        StatusMessage = string.Empty;

        Devices.Clear();
        Roles.Clear();
        Users.Clear();

        try
        {
            var allDevices = _remoteDevices;
            if (group != null || createMode)
            {
                var optionGroups = await _deviceCatalog.GetRemoteDeviceGroupOptionsAsync(_serverId, null, CancellationToken.None);
                var assignableGroupIds = optionGroups
                    .Select(item => item.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var deviceGroupPairs = await Task.WhenAll(allDevices.Select(async device =>
                {
                    if (device.RemoteDeviceId is not int remoteDeviceId || remoteDeviceId <= 0)
                    {
                        return (device, groups: device.Groups);
                    }

                    var groups = await _deviceCatalog.GetRemoteDeviceGroupsForDeviceAsync(_serverId, remoteDeviceId, CancellationToken.None);
                    return (device, groups);
                }));

                foreach (var pair in deviceGroupPairs)
                {
                    var descriptor = CloneDescriptorWithGroups(pair.device, pair.groups);
                    var isSelected = group?.IsSystem == true || (group != null && DeviceBelongsToGroup(descriptor, group.Id));
                    var item = new RemoteGroupDeviceSelectionItemViewModel(descriptor, isSelected, assignableGroupIds);
                    item.PropertyChanged += HandleSelectionItemPropertyChanged;
                    Devices.Add(item);
                }
            }

            if (CanManageAccounts)
            {
                foreach (var role in _managedRoles)
                {
                    var isSelected = group != null && role.DeviceGroups.Any(item => string.Equals(item.Id.ToString(), group.Id, StringComparison.OrdinalIgnoreCase));
                    var item = new RemoteGroupRoleSelectionItemViewModel(role, isSelected);
                    item.PropertyChanged += HandleSelectionItemPropertyChanged;
                    Roles.Add(item);
                }

                foreach (var user in _managedUsers)
                {
                    var isSelected = group != null && user.DirectDeviceGroups.Any(item => string.Equals(item.Id.ToString(), group.Id, StringComparison.OrdinalIgnoreCase));
                    var item = new RemoteGroupUserSelectionItemViewModel(user, isSelected);
                    item.PropertyChanged += HandleSelectionItemPropertyChanged;
                    Users.Add(item);
                }
            }

            ApplyDeviceFilter();
            ApplyRoleFilter();
            ApplyUserFilter();
            UpdateSelectedCounts();
            IsEditorOpen = true;
            OnPropertyChanged(nameof(GroupEditorTitle));
        }
        catch (Exception ex)
        {
            CloseEditor();
            StatusMessage = ex.Message;
        }
    }

    private void HandleSelectionItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RemoteGroupDeviceSelectionItemViewModel.IsSelected) ||
            args.PropertyName == nameof(RemoteGroupRoleSelectionItemViewModel.IsSelected) ||
            args.PropertyName == nameof(RemoteGroupUserSelectionItemViewModel.IsSelected))
        {
            UpdateSelectedCounts();
        }
    }

    private async Task SaveDeviceAssignmentsAsync(string groupId)
    {
        foreach (var device in Devices)
        {
            if (device.Descriptor.RemoteDeviceId is not int remoteDeviceId || remoteDeviceId <= 0)
            {
                continue;
            }

            var currentGroups = await _deviceCatalog.GetRemoteDeviceGroupsForDeviceAsync(_serverId, remoteDeviceId, CancellationToken.None);
            var groupIds = currentGroups
                .Select(item => item.Id)
                .Where(id => !string.Equals(id, groupId, StringComparison.OrdinalIgnoreCase))
                .Where(id => device.AssignableGroupIds.Count == 0 || device.AssignableGroupIds.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (device.IsSelected)
            {
                groupIds.Add(groupId);
            }

            await _deviceCatalog.SetRemoteDeviceGroupsAsync(
                device.Descriptor,
                groupIds.Distinct(StringComparer.OrdinalIgnoreCase),
                CancellationToken.None);
        }
    }

    private async Task SaveRoleAssignmentsAsync(string groupId)
    {
        foreach (var role in Roles)
        {
            var currentGroupIds = role.Model.DeviceGroups
                .Select(item => item.Id)
                .Where(id => id > 0)
                .ToHashSet();

            var numericGroupId = ParseGroupId(groupId);
            if (numericGroupId <= 0)
            {
                continue;
            }

            if (role.IsSelected)
            {
                currentGroupIds.Add(numericGroupId);
            }
            else
            {
                currentGroupIds.Remove(numericGroupId);
            }

            var request = new AgentRoleSaveRequest
            {
                Name = role.Model.Name,
                Description = role.Model.Description,
                Permissions = role.Model.Permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                DeviceGroupIds = currentGroupIds.OrderBy(item => item).ToList()
            };

            var updatedRole = await _agentSessions.UpdateRoleAsync(_serverId, role.Model.Id, request, CancellationToken.None);
            if (updatedRole == null)
            {
                throw new InvalidOperationException($"保存角色 {role.Model.Name} 的分组授权失败");
            }
        }
    }

    private async Task SaveUserAssignmentsAsync(string groupId)
    {
        foreach (var user in Users)
        {
            var currentGroupIds = user.Model.DirectDeviceGroups
                .Select(item => item.Id)
                .Where(id => id > 0)
                .ToHashSet();

            var numericGroupId = ParseGroupId(groupId);
            if (numericGroupId <= 0)
            {
                continue;
            }

            if (user.IsSelected)
            {
                currentGroupIds.Add(numericGroupId);
            }
            else
            {
                currentGroupIds.Remove(numericGroupId);
            }

            var request = new AgentUserSaveRequest
            {
                Username = user.Model.Username,
                IsActive = user.Model.IsActive,
                Password = string.Empty,
                RoleIds = user.Model.Roles.Select(item => item.Id).Distinct().ToList(),
                DeviceGroupIds = currentGroupIds.OrderBy(item => item).ToList()
            };

            var updatedUser = await _agentSessions.UpdateUserAsync(_serverId, user.Model.Id, request, CancellationToken.None);
            if (updatedUser == null)
            {
                throw new InvalidOperationException($"保存用户 {user.Model.Username} 的分组授权失败");
            }
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

    private void ApplyDeviceFilter()
    {
        var keyword = DeviceSearchKeyword.Trim();
        foreach (var device in Devices)
        {
            device.IsVisible = string.IsNullOrWhiteSpace(keyword) ||
                device.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                device.Serial.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    private void ApplyRoleFilter()
    {
        var keyword = RoleSearchKeyword.Trim();
        foreach (var role in Roles)
        {
            role.IsVisible = string.IsNullOrWhiteSpace(keyword) ||
                role.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                role.Description.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    private void ApplyUserFilter()
    {
        var keyword = UserSearchKeyword.Trim();
        foreach (var user in Users)
        {
            user.IsVisible = string.IsNullOrWhiteSpace(keyword) ||
                user.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                user.RoleSummary.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    private void UpdateSelectedCounts()
    {
        SelectedDeviceCount = Devices.Count(item => item.IsSelected);
        SelectedRoleCount = Roles.Count(item => item.IsSelected);
        SelectedUserCount = Users.Count(item => item.IsSelected);
    }

    private static DeviceDescriptor CloneDescriptorWithGroups(DeviceDescriptor device, IReadOnlyList<DeviceGroupDescriptor> groups)
    {
        return new DeviceDescriptor
        {
            Id = device.Id,
            ProviderId = device.ProviderId,
            ProviderName = device.ProviderName,
            Name = device.Name,
            Serial = device.Serial,
            SourceKind = device.SourceKind,
            ConnectionType = device.ConnectionType,
            Status = device.Status,
            IsConnected = device.IsConnected,
            Capabilities = device.Capabilities,
            RemoteDeviceId = device.RemoteDeviceId,
            Groups = groups
        };
    }

    private static bool DeviceBelongsToGroup(DeviceDescriptor device, string groupId)
    {
        return device.Groups.Any(item => string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseGroupId(string groupId)
    {
        return int.TryParse(groupId, out var value) ? value : 0;
    }
}

/// <summary>
/// 远程设备分组卡片 ViewModel
/// </summary>
public partial class RemoteDeviceGroupCardViewModel(
    DeviceGroupDescriptor group,
    int deviceCount,
    Func<RemoteDeviceGroupCardViewModel, Task> manageAction,
    Func<RemoteDeviceGroupCardViewModel, Task> deleteAction) : ViewModelBase
{
    private readonly Func<RemoteDeviceGroupCardViewModel, Task> _manageAction = manageAction;
    private readonly Func<RemoteDeviceGroupCardViewModel, Task> _deleteAction = deleteAction;

    public string Id { get; } = group.Id;
    public string Name { get; } = group.Name;
    public string Description { get; } = group.Description;
    public int SortOrder { get; } = group.SortOrder;
    public bool IsSystem { get; } = group.IsSystem;
    public int DeviceCount { get; } = deviceCount;
    public int RoleCount { get; } = group.RoleCount;
    public int UserCount { get; } = group.UserCount;
    public bool CanDelete => !IsSystem;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string ManageButtonText => IsSystem ? "管理授权" : "管理";
    public string EmptyDescription => "暂无描述";
    public string SystemHint => "系统全量范围组，不可改名、删除，也不会作为普通业务分组显示在首页。";

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public DeviceGroupDescriptor ToDescriptor()
    {
        return new DeviceGroupDescriptor
        {
            Id = Id,
            Name = Name,
            Description = Description,
            SortOrder = SortOrder,
            DeviceCount = DeviceCount,
            RoleCount = RoleCount,
            UserCount = UserCount,
            IsSystem = IsSystem
        };
    }

    [RelayCommand]
    private Task Manage()
    {
        return _manageAction(this);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task Delete()
    {
        return _deleteAction(this);
    }
}

/// <summary>
/// 远程设备分组编辑中的设备选择项
/// </summary>
public partial class RemoteGroupDeviceSelectionItemViewModel : ViewModelBase
{
    public RemoteGroupDeviceSelectionItemViewModel(
        DeviceDescriptor descriptor,
        bool isSelected,
        IReadOnlyCollection<string> assignableGroupIds)
    {
        Descriptor = descriptor;
        AssignableGroupIds = assignableGroupIds;
        Name = string.IsNullOrWhiteSpace(descriptor.Name) ? descriptor.Serial : descriptor.Name;
        Serial = descriptor.Serial;
        Meta = string.IsNullOrWhiteSpace(descriptor.Serial) ? descriptor.ConnectionType : descriptor.Serial;
        IsSelected = isSelected;
    }

    public DeviceDescriptor Descriptor { get; }
    public IReadOnlyCollection<string> AssignableGroupIds { get; }
    public string Name { get; }
    public string Serial { get; }
    public string Meta { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;
}

/// <summary>
/// 远程设备分组编辑中的角色选择项
/// </summary>
public partial class RemoteGroupRoleSelectionItemViewModel(AgentRoleDto model, bool isSelected) : ViewModelBase
{
    public AgentRoleDto Model { get; } = model;
    public string Name => Model.Name;
    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? "暂无描述" : Model.Description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;
}

/// <summary>
/// 远程设备分组编辑中的用户选择项
/// </summary>
public partial class RemoteGroupUserSelectionItemViewModel(AgentAccountUserDto model, bool isSelected) : ViewModelBase
{
    public AgentAccountUserDto Model { get; } = model;
    public string Name => Model.Username;
    public string RoleSummary => Model.Roles.Count == 0 ? "未分配角色" : string.Join(" / ", Model.Roles.Select(item => item.Name));

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;
}
