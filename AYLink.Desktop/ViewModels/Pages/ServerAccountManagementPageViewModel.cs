using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AYLink.Core.Agent;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class ServerAccountManagementPageViewModel : PageViewModelBase<ServerAccountManagementNavigationArgs>
{
    public override string PageKey => "ServerAccountManagement";
    public override string Title
    {
        get
        {
            var suffix = Services.Localization.LocalizationManager.Instance.GetString("ServerSettings.AccountManagementTitle", "账户管理");
            return string.IsNullOrWhiteSpace(ServerName) ? suffix : $"{ServerName} - {suffix}";
        }
    }

    private readonly AgentSessionService _agentSessions = AgentSessionService.Instance;
    private string _serverId = string.Empty;

    [ObservableProperty]
    public partial string ServerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAccountDataLoading { get; set; }

    [ObservableProperty]
    public partial bool IsAccountSaving { get; set; }

    [ObservableProperty]
    public partial string AccountStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewUserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewUserPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewRoleName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewRoleDescription { get; set; } = string.Empty;

    public ObservableCollection<AgentAccountUserItemViewModel> ManagedUsers { get; } = [];
    public ObservableCollection<AgentRoleItemViewModel> ManagedRoles { get; } = [];
    public ObservableCollection<SelectableRoleOptionViewModel> NewUserRoleOptions { get; } = [];
    public ObservableCollection<SelectablePermissionOptionViewModel> NewRolePermissionOptions { get; } = [];

    public bool HasAccountEntries => ManagedUsers.Count > 0 || ManagedRoles.Count > 0;
    public bool HasManagedUsers => ManagedUsers.Count > 0;
    public bool HasManagedRoles => ManagedRoles.Count > 0;

    protected override void OnNavigatedTo(ServerAccountManagementNavigationArgs args)
    {
        _serverId = args.ServerId;
        ServerName = args.ServerName;
        OnPropertyChanged(nameof(Title));
        _ = LoadAccountDataAsync();
    }

    [RelayCommand]
    private static void BackToServerSettings()
    {
        NavigationService.Instance.GoBack();
    }

    [RelayCommand]
    private async Task RefreshAccountManagement()
    {
        await LoadAccountDataAsync();
    }

    [RelayCommand]
    private async Task CreateUser()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var username = NewUserName.Trim();
        var password = NewUserPassword.Trim();
        var selectedRoleIds = NewUserRoleOptions.Where(option => option.IsSelected).Select(option => option.Id).ToList();
        if (string.IsNullOrWhiteSpace(username))
        {
            AccountStatusMessage = localizer.GetString("ServerAccountManagement.UsernameRequired", "请先填写用户名");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            AccountStatusMessage = localizer.GetString("ServerAccountManagement.InitialPasswordRequired", "请先填写初始密码");
            return;
        }

        if (selectedRoleIds.Count == 0)
        {
            AccountStatusMessage = localizer.GetString("ServerAccountManagement.RoleRequired", "至少选择一个角色");
            return;
        }

        IsAccountSaving = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var request = new AgentUserSaveRequest
            {
                Username = username,
                Password = password,
                RoleIds = selectedRoleIds,
                DeviceGroupIds = []
            };

            var user = await _agentSessions.CreateUserAsync(_serverId, request);
            if (user == null)
            {
                AccountStatusMessage = localizer.GetString("ServerAccountManagement.CreateUserFailed", "创建账号失败");
                return;
            }

            NewUserName = string.Empty;
            NewUserPassword = string.Empty;
            foreach (var option in NewUserRoleOptions)
            {
                option.IsSelected = false;
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                string.Format(localizer.GetString("ServerAccountManagement.UserCreated", "已创建账号 {0}"), user.Username));

            await LoadAccountDataAsync();
        }
        finally
        {
            IsAccountSaving = false;
        }
    }

    [RelayCommand]
    private async Task CreateRole()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var roleName = NewRoleName.Trim();
        var permissions = NewRolePermissionOptions.Where(option => option.IsSelected).Select(option => option.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (string.IsNullOrWhiteSpace(roleName))
        {
            AccountStatusMessage = localizer.GetString("ServerAccountManagement.RoleNameRequired", "请先填写角色名称");
            return;
        }

        if (permissions.Count == 0)
        {
            AccountStatusMessage = localizer.GetString("ServerAccountManagement.PermissionRequired", "至少选择一个权限");
            return;
        }

        IsAccountSaving = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var request = new AgentRoleSaveRequest
            {
                Name = roleName,
                Description = NewRoleDescription.Trim(),
                Permissions = permissions,
                DeviceGroupIds = []
            };

            var role = await _agentSessions.CreateRoleAsync(_serverId, request);
            if (role == null)
            {
                AccountStatusMessage = localizer.GetString("ServerAccountManagement.CreateRoleFailed", "创建角色失败");
                return;
            }

            NewRoleName = string.Empty;
            NewRoleDescription = string.Empty;
            foreach (var option in NewRolePermissionOptions)
            {
                option.IsSelected = false;
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                string.Format(localizer.GetString("ServerAccountManagement.RoleCreated", "已创建角色 {0}"), role.Name));

            await LoadAccountDataAsync();
        }
        finally
        {
            IsAccountSaving = false;
        }
    }

    private async Task LoadAccountDataAsync()
    {
        if (string.IsNullOrWhiteSpace(_serverId))
        {
            return;
        }

        IsAccountDataLoading = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var data = await _agentSessions.GetAccountDataAsync(_serverId);
            if (data == null)
            {
                AccountStatusMessage = LocalizationManager.Instance.GetString("ServerAccountManagement.LoadFailed", "加载账户管理数据失败");
                return;
            }

            RebuildManagedRoles(data.Roles, data.AvailablePermissions);
            RebuildManagedUsers(data.Users, data.Roles);
            RebuildNewUserRoleOptions(data.Roles);
            RebuildNewRolePermissionOptions(data.AvailablePermissions);

            OnPropertyChanged(nameof(HasAccountEntries));
            OnPropertyChanged(nameof(HasManagedUsers));
            OnPropertyChanged(nameof(HasManagedRoles));
        }
        finally
        {
            IsAccountDataLoading = false;
        }
    }

    private void RebuildManagedUsers(IEnumerable<AgentAccountUserDto> users, IEnumerable<AgentRoleDto> roles)
    {
        var roleSummaries = roles
            .Select(role => new AgentRoleSummaryDto
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description
            })
            .OrderBy(role => role.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ManagedUsers.Clear();
        foreach (var user in users.OrderBy(item => item.Username, StringComparer.CurrentCultureIgnoreCase))
        {
            ManagedUsers.Add(new AgentAccountUserItemViewModel(user, roleSummaries, SaveUserAsync, ToggleUserActiveAsync, ResetUserPasswordAsync, DeleteUserAsync));
        }
    }

    private void RebuildManagedRoles(IEnumerable<AgentRoleDto> roles, IEnumerable<AgentPermissionDto> permissions)
    {
        var permissionList = permissions
            .OrderBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ManagedRoles.Clear();
        foreach (var role in roles.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ManagedRoles.Add(new AgentRoleItemViewModel(role, permissionList, SaveRoleAsync));
        }
    }

    private void RebuildNewUserRoleOptions(IEnumerable<AgentRoleDto> roles)
    {
        var selectedIds = NewUserRoleOptions.Where(option => option.IsSelected).Select(option => option.Id).ToHashSet();
        NewUserRoleOptions.Clear();
        foreach (var role in roles.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            NewUserRoleOptions.Add(new SelectableRoleOptionViewModel(role.Id, role.Name, role.Description, selectedIds.Contains(role.Id)));
        }
    }

    private void RebuildNewRolePermissionOptions(IEnumerable<AgentPermissionDto> permissions)
    {
        var selectedCodes = NewRolePermissionOptions.Where(option => option.IsSelected).Select(option => option.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        NewRolePermissionOptions.Clear();
        foreach (var permission in permissions.OrderBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase))
        {
            NewRolePermissionOptions.Add(new SelectablePermissionOptionViewModel(permission.Code, permission.Description, selectedCodes.Contains(permission.Code)));
        }
    }

    private async Task SaveUserAsync(AgentAccountUserItemViewModel item)
    {
        var localizer = LocalizationManager.Instance;
        IsAccountSaving = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var request = new AgentUserSaveRequest
            {
                Username = item.Username.Trim(),
                IsActive = item.IsActive,
                Password = string.Empty,
                RoleIds = item.RoleOptions.Where(option => option.IsSelected).Select(option => option.Id).ToList(),
                DeviceGroupIds = item.DirectDeviceGroupIds.ToList()
            };

            if (request.RoleIds.Count == 0)
            {
                AccountStatusMessage = string.Format(localizer.GetString("ServerAccountManagement.UserRoleRequired", "账号 {0} 至少需要一个角色"), item.Username);
                return;
            }

            var user = await _agentSessions.UpdateUserAsync(_serverId, item.Id, request);
            if (user == null)
            {
                AccountStatusMessage = string.Format(localizer.GetString("ServerAccountManagement.UserSaveFailed", "保存账号 {0} 失败"), item.Username);
                return;
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                string.Format(localizer.GetString("ServerAccountManagement.UserUpdated", "已更新账号 {0}"), user.Username));
            await LoadAccountDataAsync();
        }
        finally
        {
            IsAccountSaving = false;
        }
    }

    private async Task ToggleUserActiveAsync(AgentAccountUserItemViewModel item)
    {
        var localizer = LocalizationManager.Instance;
        IsAccountSaving = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var success = await _agentSessions.SetUserActiveAsync(_serverId, item.Id, item.IsActive);
            if (!success)
            {
                AccountStatusMessage = string.Format(localizer.GetString("ServerAccountManagement.UserStateUpdateFailed", "更新账号 {0} 状态失败"), item.Username);
                item.IsActive = !item.IsActive;
                return;
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                string.Format(localizer.GetString("ServerAccountManagement.UserStateUpdated", "已更新账号 {0} 状态"), item.Username));
            await LoadAccountDataAsync();
        }
        finally
        {
            IsAccountSaving = false;
        }
    }

    private async Task ResetUserPasswordAsync(AgentAccountUserItemViewModel item)
    {
        var localizer = LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "newPassword", Watermark = localizer.GetString("ServerAccountManagement.NewPasswordPlaceholder", "新密码（留空则自动生成）"), IsPassword = true }
        };

        var (result, data) = await DialogService.ShowInputDialogAsync(
            localizer.GetString("ServerSettings.ResetPasswordTitle", "重置密码"),
            string.Format(localizer.GetString("ServerAccountManagement.ResetPasswordPrompt", "为账号 {0} 输入新密码，留空则自动生成随机密码。"), item.Username),
            fields,
            localizer.GetString("ServerAccountManagement.ResetPasswordButton", "重置"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary)
        {
            return;
        }

        IsAccountSaving = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var response = await _agentSessions.ResetUserPasswordAsync(_serverId, item.Id, GetFieldValue(data, "newPassword"));
            if (response == null)
            {
                AccountStatusMessage = string.Format(localizer.GetString("ServerAccountManagement.ResetPasswordFailed", "重置账号 {0} 密码失败"), item.Username);
                return;
            }

            await DialogService.ShowMessageAsync(
                localizer.GetString("ServerAccountManagement.PasswordResetTitle", "密码已重置"),
                string.Format(localizer.GetString("ServerAccountManagement.PasswordResetMessage", "账号 {0} 的新密码：{1}"), item.Username, response.Password),
                localizer.GetString("Dialog.OK", "确定"));
        }
        finally
        {
            IsAccountSaving = false;
        }
    }

    private async Task DeleteUserAsync(AgentAccountUserItemViewModel item)
    {
        var localizer = LocalizationManager.Instance;
        var result = await DialogService.ShowMessageAsync(
            localizer.GetString("ServerAccountManagement.DeleteUserTitle", "删除账号"),
            string.Format(localizer.GetString("ServerAccountManagement.DeleteUserConfirm", "确定要删除账号 {0} 吗？"), item.Username),
            localizer.GetString("Common.Delete", "删除"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary)
        {
            return;
        }

        IsAccountSaving = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var success = await _agentSessions.DeleteUserAsync(_serverId, item.Id);
            if (!success)
            {
                AccountStatusMessage = string.Format(localizer.GetString("ServerAccountManagement.DeleteUserFailed", "删除账号 {0} 失败"), item.Username);
                return;
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                string.Format(localizer.GetString("ServerAccountManagement.UserDeleted", "已删除账号 {0}"), item.Username));
            await LoadAccountDataAsync();
        }
        finally
        {
            IsAccountSaving = false;
        }
    }

    private async Task SaveRoleAsync(AgentRoleItemViewModel item)
    {
        var localizer = LocalizationManager.Instance;
        IsAccountSaving = true;
        AccountStatusMessage = string.Empty;

        try
        {
            var request = new AgentRoleSaveRequest
            {
                Name = item.Name.Trim(),
                Description = item.Description.Trim(),
                Permissions = item.PermissionOptions.Where(option => option.IsSelected).Select(option => option.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                DeviceGroupIds = item.DeviceGroupIds.ToList()
            };

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                AccountStatusMessage = localizer.GetString("ServerAccountManagement.RoleNameEmpty", "角色名称不能为空");
                return;
            }

            if (request.Permissions.Count == 0)
            {
                AccountStatusMessage = string.Format(localizer.GetString("ServerAccountManagement.RolePermissionRequired", "角色 {0} 至少需要一个权限"), item.Name);
                return;
            }

            var role = await _agentSessions.UpdateRoleAsync(_serverId, item.Id, request);
            if (role == null)
            {
                AccountStatusMessage = string.Format(localizer.GetString("ServerAccountManagement.RoleSaveFailed", "保存角色 {0} 失败"), item.Name);
                return;
            }

            NotificationService.Instance.ShowSuccess(
                localizer.GetString("Dialog.Success", "成功"),
                string.Format(localizer.GetString("ServerAccountManagement.RoleUpdated", "已更新角色 {0}"), role.Name));
            await LoadAccountDataAsync();
        }
        finally
        {
            IsAccountSaving = false;
        }
    }

    private static string GetFieldValue(IReadOnlyDictionary<string, string> data, string key)
        => data.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
}

public partial class SelectableRoleOptionViewModel(int id, string name, string description, bool isSelected) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}

public partial class SelectablePermissionOptionViewModel(string code, string description, bool isSelected, bool canEdit = true) : ObservableObject
{
    public string Code { get; } = code;
    public string Description { get; } = description;
    public bool CanEdit { get; } = canEdit;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}

public partial class AgentAccountUserItemViewModel : ObservableObject
{
    private readonly Func<AgentAccountUserItemViewModel, Task> _saveAction;
    private readonly Func<AgentAccountUserItemViewModel, Task> _toggleActiveAction;
    private readonly Func<AgentAccountUserItemViewModel, Task> _resetPasswordAction;
    private readonly Func<AgentAccountUserItemViewModel, Task> _deleteAction;

    public AgentAccountUserItemViewModel(
        AgentAccountUserDto model,
        IEnumerable<AgentRoleSummaryDto> allRoles,
        Func<AgentAccountUserItemViewModel, Task> saveAction,
        Func<AgentAccountUserItemViewModel, Task> toggleActiveAction,
        Func<AgentAccountUserItemViewModel, Task> resetPasswordAction,
        Func<AgentAccountUserItemViewModel, Task> deleteAction)
    {
        Id = model.Id;
        Username = model.Username;
        IsActive = model.IsActive;
        DirectDeviceGroupIds = (model.DirectDeviceGroups ?? []).Select(group => group.Id).ToList();
        EffectiveScopeSummary = string.Format(
            LocalizationManager.Instance.GetString("ServerAccountManagement.EffectiveScopeSummary", "最终生效 {0} 个分组 / {1} 台设备"),
            model.EffectiveDeviceGroupCount, model.EffectiveDeviceCount);
        _saveAction = saveAction;
        _toggleActiveAction = toggleActiveAction;
        _resetPasswordAction = resetPasswordAction;
        _deleteAction = deleteAction;

        var selectedRoleIds = (model.Roles ?? []).Select(role => role.Id).ToHashSet();
        foreach (var role in allRoles.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            RoleOptions.Add(new SelectableRoleOptionViewModel(role.Id, role.Name, role.Description, selectedRoleIds.Contains(role.Id)));
        }
    }

    public int Id { get; }
    public List<int> DirectDeviceGroupIds { get; }
    public string EffectiveScopeSummary { get; }
    public ObservableCollection<SelectableRoleOptionViewModel> RoleOptions { get; } = [];

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [RelayCommand]
    private Task Save()
        => _saveAction(this);

    [RelayCommand]
    private Task ToggleActive()
        => _toggleActiveAction(this);

    [RelayCommand]
    private Task ResetPassword()
        => _resetPasswordAction(this);

    [RelayCommand]
    private Task Delete()
        => _deleteAction(this);
}

public partial class AgentRoleItemViewModel : ObservableObject
{
    private readonly Func<AgentRoleItemViewModel, Task> _saveAction;

    public AgentRoleItemViewModel(
        AgentRoleDto model,
        IEnumerable<AgentPermissionDto> permissions,
        Func<AgentRoleItemViewModel, Task> saveAction)
    {
        Id = model.Id;
        Name = model.Name;
        Description = model.Description;
        IsInternal = model.IsInternal;
        DeviceGroupIds = (model.DeviceGroups ?? []).Select(group => group.Id).ToList();
        _saveAction = saveAction;

        var selectedPermissions = (model.Permissions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in permissions.OrderBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase))
        {
            PermissionOptions.Add(new SelectablePermissionOptionViewModel(
                permission.Code,
                permission.Description,
                selectedPermissions.Contains(permission.Code),
                !model.IsInternal));
        }
    }

    public int Id { get; }
    public bool IsInternal { get; }
    public List<int> DeviceGroupIds { get; }
    public ObservableCollection<SelectablePermissionOptionViewModel> PermissionOptions { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [RelayCommand]
    private Task Save()
        => _saveAction(this);
}
