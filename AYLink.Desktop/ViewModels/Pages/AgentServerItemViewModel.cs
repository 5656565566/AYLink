using System;
using System.Threading.Tasks;
using AYLink.Desktop.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class AgentServerItemViewModel(
    AgentServerRuntimeSnapshot runtime,
    Func<AgentServerRuntimeSnapshot, Task> refreshAction,
    Func<AgentServerRuntimeSnapshot, Task> loginAction,
    Func<AgentServerRuntimeSnapshot, Task> editAction,
    Func<AgentServerRuntimeSnapshot, Task> deleteAction,
    Func<AgentServerRuntimeSnapshot, Task> addDeviceAction,
    Func<AgentServerRuntimeSnapshot, Task> openSettingsAction) : ViewModelBase
{
    private readonly AgentServerRuntimeSnapshot _runtime = runtime;
    private readonly Func<AgentServerRuntimeSnapshot, Task> _refreshAction = refreshAction;
    private readonly Func<AgentServerRuntimeSnapshot, Task> _loginAction = loginAction;
    private readonly Func<AgentServerRuntimeSnapshot, Task> _editAction = editAction;
    private readonly Func<AgentServerRuntimeSnapshot, Task> _deleteAction = deleteAction;
    private readonly Func<AgentServerRuntimeSnapshot, Task> _addDeviceAction = addDeviceAction;
    private readonly Func<AgentServerRuntimeSnapshot, Task> _openSettingsAction = openSettingsAction;

    public string Id => _runtime.Id;
    public string DisplayName => _runtime.DisplayName;
    public string BaseUrl => _runtime.BaseUrl;
    
    public string DisplayUrl
    {
        get
        {
            if (Uri.TryCreate(_runtime.BaseUrl, UriKind.Absolute, out var uri))
            {
                return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            }
            return _runtime.BaseUrl;
        }
    }
    
    public string Username => _runtime.Username;
    public string LastKnownUserName => _runtime.LastKnownUserName;
    public string StateText => _runtime.StateText;
    public string LastError => _runtime.LastError;
    public string LastSyncText => _runtime.LastSyncText;
    public bool CanOpenSettings => _runtime.CanOpenSettings;
    public bool CanManageDevices => _runtime.State == AgentServerConnectionState.Connected;
    public bool IsConnecting => _runtime.State == AgentServerConnectionState.Connecting;
    public bool CanLogin => _runtime.State == AgentServerConnectionState.Unauthorized || 
                            _runtime.State == AgentServerConnectionState.Error || 
                            _runtime.State == AgentServerConnectionState.Unknown;
    public bool HasError => !string.IsNullOrEmpty(_runtime.LastError) && _runtime.State == AgentServerConnectionState.Error;

    [RelayCommand]
    private Task RefreshServer() => _refreshAction(_runtime);

    [RelayCommand]
    private Task LoginServer() => _loginAction(_runtime);

    [RelayCommand]
    private Task EditServer() => _editAction(_runtime);

    [RelayCommand]
    private Task DeleteServer() => _deleteAction(_runtime);

    [RelayCommand]
    private Task AddDevice() => _addDeviceAction(_runtime);

    [RelayCommand]
    private Task OpenSettings() => _openSettingsAction(_runtime);
}
