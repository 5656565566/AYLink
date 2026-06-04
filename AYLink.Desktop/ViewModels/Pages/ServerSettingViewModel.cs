using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AYLink.Desktop.ViewModels.Pages;

public class ServerSettingNavigationArgs : NavigationArgs
{
    public string ServerId { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
}

public class ServerAccountManagementNavigationArgs : NavigationArgs
{
    public string ServerId { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
}

public class ServerRemoteDeviceGroupsNavigationArgs : NavigationArgs
{
    public string ServerId { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public bool CanManageAccounts { get; init; }
}

public partial class ServerSettingViewModel : PageViewModelBase<ServerSettingNavigationArgs>
{
    public override string PageKey => "ServerSetting";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("ServerSettingPage.Title", "服务器设置");

    [ObservableProperty]
    public partial string ServerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AgentServerConfig ServerConfig { get; set; } = new();

    [ObservableProperty]
    public partial bool HasMultipleServers { get; set; }

    [ObservableProperty]
    public partial string CurrentLoginUserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanChangePassword { get; set; }

    [ObservableProperty]
    public partial bool CanManageAccounts { get; set; }

    [ObservableProperty]
    public partial bool CanManageRemoteDeviceGroups { get; set; }

    private string _serverId = string.Empty;
    private readonly AgentSessionService _agentSessions = AgentSessionService.Instance;

    public ObservableCollection<AgentServerIceServerViewModel> LocalIceServers { get; } = [];
    public ObservableCollection<AgentServerIceServerViewModel> GlobalIceServers { get; } = [];

    protected override void OnNavigatedTo(ServerSettingNavigationArgs args)
    {
        _serverId = args.ServerId;
        ServerName = args.ServerName;
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (string.IsNullOrEmpty(_serverId))
        {
            return;
        }

        HasMultipleServers = _agentSessions.Servers.Count > 1;

        var server = _agentSessions.GetServerConfig(_serverId);
        var runtime = _agentSessions.FindServer(_serverId);
        if (server != null)
        {
            ServerConfig = server;

            LocalIceServers.Clear();
            var normalizedLocalServers = NormalizeIceServers(server.LocalIceServers);
            server.LocalIceServers = normalizedLocalServers.Select(CloneIceServer).ToList();
            foreach (var ice in server.LocalIceServers)
            {
                LocalIceServers.Add(new AgentServerIceServerViewModel(ice, RemoveLocalIceServer));
            }

            GlobalIceServers.Clear();
            var normalizedGlobalServers = NormalizeIceServers(server.GlobalIceServers);
            server.GlobalIceServers = normalizedGlobalServers.Select(CloneIceServer).ToList();
            foreach (var ice in server.GlobalIceServers)
            {
                GlobalIceServers.Add(new AgentServerIceServerViewModel(ice, RemoveGlobalIceServer));
            }

            UpdateRemoveButtonStates();
        }

        if (runtime != null)
        {
            CurrentLoginUserName = string.IsNullOrWhiteSpace(runtime.Config.LastKnownUserName)
                ? runtime.Config.Username
                : runtime.Config.LastKnownUserName;
            CanChangePassword = runtime.LastPermissions.Contains("accounts.change-password", StringComparer.OrdinalIgnoreCase);
            CanManageAccounts = runtime.LastPermissions.Contains("accounts.manage", StringComparer.OrdinalIgnoreCase);
            CanManageRemoteDeviceGroups =
                runtime.LastPermissions.Contains("accounts.manage", StringComparer.OrdinalIgnoreCase) ||
                runtime.LastPermissions.Contains("devices.manage", StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            CurrentLoginUserName = string.Empty;
            CanChangePassword = false;
            CanManageAccounts = false;
            CanManageRemoteDeviceGroups = false;
        }
    }

    private void RemoveLocalIceServer(AgentServerIceServerViewModel vm)
    {
        LocalIceServers.Remove(vm);
        UpdateRemoveButtonStates();
    }

    private void RemoveGlobalIceServer(AgentServerIceServerViewModel vm)
    {
        GlobalIceServers.Remove(vm);
        UpdateRemoveButtonStates();
    }

    private void UpdateRemoveButtonStates()
    {
        foreach (var item in LocalIceServers)
        {
            item.CanRemove = LocalIceServers.Count > 1;
        }

        foreach (var item in GlobalIceServers)
        {
            item.CanRemove = GlobalIceServers.Count > 1;
        }
    }

    private static List<AgentServerIceServerConfig> NormalizeIceServers(IEnumerable<AgentServerIceServerConfig>? servers)
    {
        var normalized = (servers ?? [])
            .Where(static item => item != null)
            .Select(static item => new AgentServerIceServerConfig
            {
                Kind = string.Equals(item.Kind, "TURN", StringComparison.OrdinalIgnoreCase) ? "TURN" : "STUN",
                Address = item.Address.Trim()
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Address))
            .GroupBy(static item => $"{item.Kind}|{item.Address}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add(new AgentServerIceServerConfig
            {
                Kind = "STUN",
                Address = "stun:stun.l.google.com:19302"
            });
        }

        return normalized;
    }

    private static AgentServerIceServerConfig CloneIceServer(AgentServerIceServerConfig source)
    {
        return new AgentServerIceServerConfig
        {
            Kind = source.Kind,
            Address = source.Address
        };
    }

    [RelayCommand]
    private void SaveConfig()
    {
        SaveConfigInternal();
    }

    private bool SaveConfigInternal()
    {
        if (string.IsNullOrEmpty(_serverId))
        {
            return false;
        }

        ServerConfig.LocalIceServers = NormalizeIceServers(LocalIceServers.Select(vm => vm.Model)).Select(CloneIceServer).ToList();
        ServerConfig.GlobalIceServers = NormalizeIceServers(GlobalIceServers.Select(vm => vm.Model)).Select(CloneIceServer).ToList();

        _agentSessions.SaveConfig();

        var localizer = Services.Localization.LocalizationManager.Instance;
        Services.Notifications.NotificationService.Instance.ShowSuccess(
            localizer.GetString("Dialog.Success", "成功"),
            localizer.GetString("ServerSettingPage.SaveSuccess", "服务器设置已保存"));

        return true;
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        ServerConfig.EnableWebRtcOverride = false;
        ServerConfig.LocalIceTransportPolicy = "all";
        ServerConfig.IceTransportPolicy = "all";
        ServerConfig.EnableHostCandidateOverride = false;
        ServerConfig.DirectHostList = string.Empty;
        ServerConfig.EnablePortMapping = false;
        ServerConfig.LocalBindPort = "5551";
        ServerConfig.ExternalPublishPort = "5551";

        LocalIceServers.Clear();
        LocalIceServers.Add(new AgentServerIceServerViewModel(new AgentServerIceServerConfig { Kind = "STUN", Address = "stun:stun.l.google.com:19302" }, RemoveLocalIceServer));

        GlobalIceServers.Clear();
        GlobalIceServers.Add(new AgentServerIceServerViewModel(new AgentServerIceServerConfig { Kind = "STUN", Address = "stun:stun.l.google.com:19302" }, RemoveGlobalIceServer));

        UpdateRemoveButtonStates();
        OnPropertyChanged(nameof(ServerConfig));
    }

    [RelayCommand]
    private void AddLocalStun()
    {
        LocalIceServers.Add(new AgentServerIceServerViewModel(new AgentServerIceServerConfig { Kind = "STUN", Address = "" }, RemoveLocalIceServer));
        UpdateRemoveButtonStates();
    }

    [RelayCommand]
    private void AddLocalTurn()
    {
        LocalIceServers.Add(new AgentServerIceServerViewModel(new AgentServerIceServerConfig { Kind = "TURN", Address = "" }, RemoveLocalIceServer));
        UpdateRemoveButtonStates();
    }

    [RelayCommand]
    private void AddGlobalStun()
    {
        GlobalIceServers.Add(new AgentServerIceServerViewModel(new AgentServerIceServerConfig { Kind = "STUN", Address = "" }, RemoveGlobalIceServer));
        UpdateRemoveButtonStates();
    }

    [RelayCommand]
    private void AddGlobalTurn()
    {
        GlobalIceServers.Add(new AgentServerIceServerViewModel(new AgentServerIceServerConfig { Kind = "TURN", Address = "" }, RemoveGlobalIceServer));
        UpdateRemoveButtonStates();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ChangePassword()
    {
        if (!CanChangePassword)
        {
            Services.Notifications.NotificationService.Instance.ShowWarning(
                Services.Localization.LocalizationManager.Instance.GetString("Dialog.Warning", "警告"),
                "当前账号没有修改密码的权限");
            return;
        }

        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "currentPassword", Watermark = "当前密码", IsRequired = true, IsPassword = true },
            new() { Key = "newPassword", Watermark = "新密码", IsRequired = true, IsPassword = true },
            new() { Key = "confirmPassword", Watermark = "确认新密码", IsRequired = true, IsPassword = true }
        };

        var (result, data) = await Services.Notifications.DialogService.ShowInputDialogAsync(
            localizer.GetString("ServerSettings.ChangePasswordTitle", "修改密码"),
            localizer.GetString("ServerSettings.ChangePasswordDescription", "修改当前登录账号的密码，修改后需要重新登录。"),
            fields,
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
        {
            return;
        }

        var currentPassword = GetFieldValue(data, "currentPassword");
        var newPassword = GetFieldValue(data, "newPassword");
        var confirmPassword = GetFieldValue(data, "confirmPassword");
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            Services.Notifications.NotificationService.Instance.ShowWarning(localizer.GetString("Dialog.Warning", "警告"), "请填写完整的密码信息");
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            Services.Notifications.NotificationService.Instance.ShowWarning(localizer.GetString("Dialog.Warning", "警告"), "两次输入的新密码不一致");
            return;
        }

        var changed = await _agentSessions.ChangePasswordAsync(_serverId, currentPassword, newPassword);
        if (!changed)
        {
            Services.Notifications.NotificationService.Instance.ShowError(localizer.GetString("Dialog.Error", "错误"), "修改密码失败");
            return;
        }

        Services.Notifications.NotificationService.Instance.ShowSuccess(localizer.GetString("Dialog.Success", "成功"), "密码已修改，请重新登录");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Logout()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var result = await Services.Notifications.DialogService.ShowMessageAsync(
            localizer.GetString("ServerSettings.LogoutTitle", "退出登录"),
            localizer.GetString("ServerSettings.LogoutDescription", "结束当前服务器会话并返回上一页。"),
            localizer.GetString("ServerSettings.LogoutTitle", "退出登录"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
        {
            return;
        }

        var success = await _agentSessions.LogoutAsync(_serverId);
        if (!success)
        {
            Services.Notifications.NotificationService.Instance.ShowError(localizer.GetString("Dialog.Error", "错误"), "退出登录失败");
            return;
        }

        Services.Notifications.NotificationService.Instance.ShowSuccess(localizer.GetString("Dialog.Success", "成功"), "已退出当前服务器登录");
        NavigationService.Instance.GoBack();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task LogoutAll()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var result = await Services.Notifications.DialogService.ShowMessageAsync(
            localizer.GetString("ServerSettings.LogoutAllTitle", "退出全部会话"),
            localizer.GetString("ServerSettings.LogoutAllDescription", "结束当前账号在该服务器上的全部登录会话。"),
            localizer.GetString("ServerSettings.LogoutAllTitle", "退出全部会话"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
        {
            return;
        }

        var success = await _agentSessions.LogoutAllAsync(_serverId);
        if (!success)
        {
            Services.Notifications.NotificationService.Instance.ShowError(localizer.GetString("Dialog.Error", "错误"), "退出全部会话失败");
            return;
        }

        Services.Notifications.NotificationService.Instance.ShowSuccess(localizer.GetString("Dialog.Success", "成功"), "已退出全部会话，请重新登录");
        NavigationService.Instance.GoBack();
    }

    [RelayCommand]
    private void OpenAccountManagement()
    {
        if (!CanManageAccounts)
        {
            Services.Notifications.NotificationService.Instance.ShowWarning(
                Services.Localization.LocalizationManager.Instance.GetString("Dialog.Warning", "警告"),
                "当前账号没有账户管理权限");
            return;
        }

        NavigationService.Instance.NavigateTo("ServerAccountManagement", new ServerAccountManagementNavigationArgs
        {
            ServerId = _serverId,
            ServerName = ServerName
        });
    }

    [RelayCommand]
    private void OpenRemoteDeviceGroups()
    {
        if (!CanManageRemoteDeviceGroups)
        {
            Services.Notifications.NotificationService.Instance.ShowWarning(
                Services.Localization.LocalizationManager.Instance.GetString("Dialog.Warning", "警告"),
                "当前账号没有远程设备分组管理权限");
            return;
        }

        NavigationService.Instance.NavigateTo("ServerRemoteDeviceGroups", new ServerRemoteDeviceGroupsNavigationArgs
        {
            ServerId = _serverId,
            ServerName = ServerName,
            CanManageAccounts = CanManageAccounts
        });
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteServer()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var title = $"{localizer.GetString("ServersPage.DeleteServer", "删除服务器")} - {ServerName}";

        var result = await Services.Notifications.DialogService.ShowMessageAsync(
            title,
            string.Format(localizer.GetString("ServersPage.DeleteServerConfirm", "确定删除服务器 {0} 吗？"), ServerName),
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result == FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
        {
            _agentSessions.RemoveServer(_serverId);
            NavigationService.Instance.GoBack();
        }
    }

    [RelayCommand]
    private void BackToHome()
    {
        if (SaveConfigInternal())
        {
            NavigationService.Instance.GoBack();
        }
    }

    private static string GetFieldValue(IReadOnlyDictionary<string, string> data, string key)
        => data.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
}

public partial class AgentServerIceServerViewModel(AgentServerIceServerConfig model, Action<AgentServerIceServerViewModel> removeAction) : ObservableObject
{
    public AgentServerIceServerConfig Model { get; } = model;
    private readonly Action<AgentServerIceServerViewModel> _removeAction = removeAction;

    [ObservableProperty]
    private bool _canRemove;

    public string Kind => Model.Kind;

    public string DisplayTitle => $"{Model.Kind} ICE 服务器";

    public string Address
    {
        get => Model.Address;
        set
        {
            if (Model.Address != value)
            {
                Model.Address = value;
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void Remove()
    {
        _removeAction?.Invoke(this);
    }
}
