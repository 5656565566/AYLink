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

public partial class ServerSettingViewModel : PageViewModelBase<ServerSettingNavigationArgs>
{
    public override string PageKey => "ServerSetting";
    public override string Title => Services.Localization.LocalizationManager.Instance.GetString("ServerSettingPage.Title", "服务器设置");

    [ObservableProperty]
    public partial string ServerName { get; set; } = string.Empty;
    private string _serverId = string.Empty;
    private readonly AgentSessionService _agentSessions = AgentSessionService.Instance;

    [ObservableProperty]
    public partial AgentServerConfig ServerConfig { get; set; } = new();

    // 是否有多个服务器 用于控制右上角的删除按钮是否显示

    [ObservableProperty]
    public partial bool HasMultipleServers { get; set; }
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
        if (string.IsNullOrEmpty(_serverId)) return;
        
        HasMultipleServers = _agentSessions.Servers.Count > 1;

        var server = _agentSessions.GetServerConfig(_serverId);
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
        if (string.IsNullOrEmpty(_serverId)) return false;

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
        OnPropertyChanged(nameof(ServerConfig)); // 通知 UI 刷新绑定
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
}

public partial class AgentServerIceServerViewModel(AgentServerIceServerConfig model, System.Action<AgentServerIceServerViewModel> removeAction) : ObservableObject
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
