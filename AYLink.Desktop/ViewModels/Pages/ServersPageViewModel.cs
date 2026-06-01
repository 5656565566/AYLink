using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AYLink.Core.Devices;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services;
using AYLink.Desktop.Services.Devices;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class ServersPageViewModel : PageViewModelBase
{
    public override string PageKey => "Servers";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("ServersPage.Title", "服务器");

    private readonly AgentSessionService _agentSessions = AgentSessionService.Instance;
    private readonly DeviceCatalogService _deviceCatalog = DeviceCatalogService.Instance;

    public ObservableCollection<AgentServerItemViewModel> Servers { get; } = [];

    [ObservableProperty]
    public partial bool HasServers { get; set; }

    public ServersPageViewModel()
    {
        _agentSessions.ServersChanged += Reload;
        Reload();
    }

    public override void OnNavigatedTo(object? parameter = null)
    {
        base.OnNavigatedTo(parameter);
        Reload();
    }

    [RelayCommand]
    private async Task AddServer()
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "name", Watermark = localizer.GetString("ServersPage.ServerName", "服务器名称"), IsRequired = true },
            new() { Key = "baseUrl", Watermark = localizer.GetString("ServersPage.ServerUrl", "服务器地址，例如 127.0.0.1:8080"), IsRequired = true },
            new() { Key = "username", Watermark = localizer.GetString("ServersPage.Username", "用户名"), IsRequired = true },
            new() { Key = "password", Watermark = localizer.GetString("ServersPage.Password", "密码"), IsRequired = true, IsPassword = true }
        };

        var (result, data) = await DialogService.ShowInputDialogAsync(
            localizer.GetString("ServersPage.AddServer", "添加服务器"),
            string.Empty,
            fields,
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (!TryGetRequired(data, "name", out var name) ||
            !TryGetRequired(data, "baseUrl", out var baseUrl) ||
            !TryGetRequired(data, "username", out var username) ||
            !TryGetRequired(data, "password", out var password))
        {
            NotificationService.Instance.ShowWarning(localizer.GetString("Dialog.Warning", "警告"), "请填写完整的服务器信息");
            return;
        }

        var success = await _agentSessions.AddServerAsync(name, baseUrl, username, password);
        if (success.State == AgentServerConnectionState.Error)
        {
            NotificationService.Instance.ShowWarning(localizer.GetString("Dialog.Warning", "警告"), success.LastError);
        }
        else
        {
            NotificationService.Instance.ShowSuccess(localizer.GetString("Dialog.Success", "成功"), localizer.GetString("ServersPage.ServerAdded", "服务器已添加"));
        }
    }

    [RelayCommand]
    private async Task RefreshAll()
    {
        foreach (var server in _agentSessions.Servers)
        {
            await _agentSessions.RefreshServerAsync(server.Config.Id);
        }
        Reload();
    }

    private async Task RefreshServerAsync(AgentServerRuntimeSnapshot snapshot)
    {
        await _agentSessions.RefreshServerAsync(snapshot.Id);
        Reload();
    }

    private async Task LoginServerAsync(AgentServerRuntimeSnapshot snapshot)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "username", Watermark = localizer.GetString("ServersPage.Username", "用户名"), Value = snapshot.Username, IsRequired = true },
            new() { Key = "password", Watermark = localizer.GetString("ServersPage.Password", "密码"), IsRequired = true, IsPassword = true }
        };

        var title = $"{localizer.GetString("ServersPage.Login", "登录服务器")} - {snapshot.DisplayName}";

        var (result, data) = await DialogService.ShowInputDialogAsync(
            title,
            string.Empty,
            fields,
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (!TryGetRequired(data, "username", out var username) ||
            !TryGetRequired(data, "password", out var password))
        {
            NotificationService.Instance.ShowWarning(localizer.GetString("Dialog.Warning", "警告"), "请输入用户名和密码");
            return;
        }

        var success = await _agentSessions.LoginAsync(snapshot.Id, username, password);
        if (!success)
        {
            NotificationService.Instance.ShowError(localizer.GetString("Dialog.Error", "错误"), localizer.GetString("ServersPage.LoginFailed", "登录失败"));
        }

        Reload();
    }

    private async Task EditServerAsync(AgentServerRuntimeSnapshot snapshot)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "name", Watermark = localizer.GetString("ServersPage.ServerName", "服务器名称"), Value = snapshot.DisplayName, IsRequired = true },
            new() { Key = "baseUrl", Watermark = localizer.GetString("ServersPage.ServerUrl", "服务器地址"), Value = snapshot.BaseUrl, IsRequired = true }
        };

        var title = $"{localizer.GetString("ServersPage.EditServer", "编辑服务器")} - {snapshot.DisplayName}";

        var (result, data) = await DialogService.ShowInputDialogAsync(
            title,
            string.Empty,
            fields,
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (!TryGetRequired(data, "name", out var name) ||
            !TryGetRequired(data, "baseUrl", out var baseUrl))
        {
            return;
        }

        await _agentSessions.UpdateServerAsync(snapshot.Id, name, baseUrl);
        Reload();
    }

    private async Task DeleteServerAsync(AgentServerRuntimeSnapshot snapshot)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var title = $"{localizer.GetString("ServersPage.DeleteServer", "删除服务器")} - {snapshot.DisplayName}";

        var result = await DialogService.ShowMessageAsync(
            title,
            string.Format(localizer.GetString("ServersPage.DeleteServerConfirm", "确定删除服务器 {0} 吗？"), snapshot.DisplayName),
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _agentSessions.RemoveServer(snapshot.Id);
        Reload();
    }

    private async Task AddDeviceAsync(AgentServerRuntimeSnapshot snapshot)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var fields = new List<InputFieldModel>
        {
            new() { Key = "serialOrIp", Watermark = localizer.GetString("ServersPage.DeviceSerialOrIp", "设备串号或 IP 地址"), IsRequired = true },
            new() { Key = "port", Watermark = localizer.GetString("ServersPage.DevicePort", "端口，默认 5555"), Value = "5555" },
            new() { Key = "pairPort", Watermark = localizer.GetString("ServersPage.PairPort", "配对端口（可选）") },
            new() { Key = "pairCode", Watermark = localizer.GetString("ServersPage.PairCode", "配对码（可选）") },
            new() { Key = "name", Watermark = localizer.GetString("ServersPage.DeviceName", "设备名称（可选）") }
        };

        var title = $"{localizer.GetString("ServersPage.AddRemoteDevice", "添加远程设备")} - {snapshot.DisplayName}";

        var (result, data) = await DialogService.ShowInputDialogAsync(
            title,
            string.Empty,
            fields,
            localizer.GetString("Dialog.OK", "确定"),
            localizer.GetString("Dialog.Cancel", "取消"));

        if (result != ContentDialogResult.Primary || !TryGetRequired(data, "serialOrIp", out var serialOrIp))
        {
            return;
        }

        int.TryParse(GetValue(data, "port"), out var port);
        int.TryParse(GetValue(data, "pairPort"), out var pairPort);
        var request = new DeviceCreationRequest
        {
            Serial = serialOrIp.Contains(':') || !serialOrIp.Contains('.')
                ? serialOrIp.Trim()
                : string.Empty,
            Host = serialOrIp.Contains('.') ? serialOrIp.Trim() : string.Empty,
            Port = port <= 0 ? 5555 : port,
            PairingPort = pairPort,
            PairingCode = GetValue(data, "pairCode"),
            Name = GetValue(data, "name")
        };

        var added = await _deviceCatalog.AddRemoteDeviceAsync(snapshot.Id, request);
        if (added == null)
        {
            NotificationService.Instance.ShowError(localizer.GetString("Dialog.Error", "错误"), localizer.GetString("ServersPage.AddRemoteDeviceFailed", "远程设备添加失败"));
            return;
        }

        NotificationService.Instance.ShowSuccess(localizer.GetString("Dialog.Success", "成功"), localizer.GetString("ServersPage.AddRemoteDeviceSuccess", "远程设备已添加"));
    }

    private async Task OpenServerSettingsAsync(AgentServerRuntimeSnapshot snapshot)
    {
        if (!snapshot.CanOpenSettings)
        {
            NotificationService.Instance.ShowWarning(
                Services.Localization.LocalizationManager.Instance.GetString("Dialog.Warning", "警告"),
                "当前账号没有查看服务器设置的权限");
            return;
        }

        NavigationService.Instance.NavigateTo("ServerSetting", new ServerSettingNavigationArgs
        {
            ServerId = snapshot.Id,
            ServerName = snapshot.DisplayName
        });
        await Task.CompletedTask;
    }

    private void Reload()
    {
        Servers.Clear();
        foreach (var runtime in _agentSessions.Servers.Select(AgentServerRuntimeSnapshot.FromRuntime))
        {
            Servers.Add(new AgentServerItemViewModel(
                runtime,
                RefreshServerAsync,
                LoginServerAsync,
                EditServerAsync,
                DeleteServerAsync,
                AddDeviceAsync,
                OpenServerSettingsAsync));
        }

        HasServers = Servers.Count > 0;
    }

    private static bool TryGetRequired(IReadOnlyDictionary<string, string> data, string key, out string value)
    {
        value = GetValue(data, key).Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> data, string key)
        => data.TryGetValue(key, out var value) ? value : string.Empty;
}

public sealed class AgentServerRuntimeSnapshot
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string BaseUrl { get; init; }
    public required string Username { get; init; }
    public required string LastKnownUserName { get; init; }
    public required AgentServerConnectionState State { get; init; }
    public required string StateText { get; init; }
    public required string LastError { get; init; }
    public required string LastSyncText { get; init; }
    public required bool CanOpenSettings { get; init; }

    public static AgentServerRuntimeSnapshot FromRuntime(AgentServerRuntime runtime)
    {
        var localizer = Services.Localization.LocalizationManager.Instance;
        var errorMsg = runtime.LastError;
        
        if (!string.IsNullOrWhiteSpace(errorMsg) && errorMsg.Equals("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            errorMsg = localizer.GetString("ServersPage.StateUnauthorized", "未登录");
        }
        
        var stateText = runtime.State switch
        {
            AgentServerConnectionState.Connected => localizer.GetString("ServersPage.StateConnected", "已连接"),
            AgentServerConnectionState.Connecting => localizer.GetString("ServersPage.StateConnecting", "连接中"),
            AgentServerConnectionState.Unauthorized => localizer.GetString("ServersPage.StateUnauthorized", "未登录"),
            AgentServerConnectionState.Error => string.IsNullOrWhiteSpace(errorMsg) ? localizer.GetString("ServersPage.StateError", "异常") : errorMsg,
            _ => localizer.GetString("ServersPage.StateDisconnected", "未连接")
        };
        
        return new AgentServerRuntimeSnapshot
        {
            Id = runtime.Config.Id,
            DisplayName = runtime.Config.DisplayName,
            BaseUrl = runtime.Config.BaseUrl,
            Username = runtime.Config.Username,
            LastKnownUserName = runtime.Config.LastKnownUserName,
            State = runtime.State,
            StateText = stateText,
            LastError = runtime.LastError,
            LastSyncText = runtime.Config.LastSyncAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? localizer.GetString("ServersPage.NeverSynced", "从未同步"),
            CanOpenSettings =
                runtime.LastPermissions.Contains("settings.view", StringComparer.OrdinalIgnoreCase) ||
                runtime.LastPermissions.Contains("settings.manage", StringComparer.OrdinalIgnoreCase)
        };
    }
}
