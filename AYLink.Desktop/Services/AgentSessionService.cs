using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Agent;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Localization;
using Newtonsoft.Json.Linq;

namespace AYLink.Desktop.Services;

/// <summary>
/// Agent 会话管理服务
/// 负责远程服务器配置加载、登录、刷新令牌与运行时状态维护
/// </summary>
public sealed class AgentSessionService
{
    /// <summary>
    /// 全局单例实例
    /// </summary>
    public static AgentSessionService Instance { get; } = new();

    private const string ConfigName = "agentServers";
    private readonly ConfigManager _configManager = ConfigManager.Instance;
    private readonly List<AgentServerRuntime> _servers = [];

    /// <summary>
    /// 服务器集合或服务器运行时状态发生变化时触发
    /// 供页面与设备聚合服务刷新展示状态
    /// </summary>
    public event Action? ServersChanged;

    /// <summary>
    /// 初始化 Agent 会话管理服务
    /// 启动时会先从本地配置中恢复已保存的服务器列表
    /// </summary>
    private AgentSessionService()
    {
        Load();
    }

    /// <summary>
    /// 当前已加载的服务器运行时集合
    /// </summary>
    public IReadOnlyList<AgentServerRuntime> Servers => _servers;

    /// <summary>
    /// 获取指定的服务器配置对象
    /// </summary>
    public AgentServerConfig? GetServerConfig(string serverId)
    {
        return FindServer(serverId)?.Config;
    }

    /// <summary>
    /// 请求保存配置文件
    /// </summary>
    public void SaveConfig()
    {
        Save();
    }

    /// <summary>
    /// 初始化所有已保存的 Agent 服务器会话
    /// 会尝试刷新已有令牌并同步基础状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var server in _servers)
        {
            try
            {
                await RefreshServerAsync(server.Config.Id, cancellationToken);
            }
            catch
            {
                
            }
        }
    }

    /// <summary>
    /// 按服务器 ID 查找运行时实例
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <returns>匹配到的服务器运行时；不存在时返回 null</returns>
    public AgentServerRuntime? FindServer(string serverId)
        => _servers.FirstOrDefault(item => item.Config.Id == serverId);

    /// <summary>
    /// 新增一个 Agent 服务器并立即执行登录
    /// </summary>
    /// <param name="displayName">服务器显示名称</param>
    /// <param name="baseUrl">服务器基础地址</param>
    /// <param name="username">登录用户名</param>
    /// <param name="password">登录密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新增后的服务器运行时</returns>
    public async Task<AgentServerRuntime> AddServerAsync(string displayName, string baseUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var config = new AgentServerConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName.Trim(),
            BaseUrl = baseUrl.Trim(),
            Username = username.Trim()
        };

        var runtime = new AgentServerRuntime(config, Save);
        _servers.Add(runtime);
        await LoginAsync(runtime.Config.Id, username, password, cancellationToken);
        Save();
        NotifyChanged();
        return runtime;
    }

    /// <summary>
    /// 更新已有 Agent 服务器的名称与基础地址
    /// 若基础地址发生变化，则会重建 API 客户端并尝试刷新登录态
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="displayName">新的显示名称</param>
    /// <param name="baseUrl">新的基础地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功找到并更新该服务器</returns>
    public async Task<bool> UpdateServerAsync(string serverId, string displayName, string baseUrl, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        var normalizedUrl = baseUrl.Trim();
        var recreateClient = !string.Equals(runtime.Config.BaseUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase);
        runtime.Config.DisplayName = displayName.Trim();
        runtime.Config.BaseUrl = normalizedUrl;

        if (recreateClient)
        {
            runtime.ResetClient();
            runtime.State = AgentServerConnectionState.Unknown;
        }

        Save();
        NotifyChanged();

        if (recreateClient && !string.IsNullOrWhiteSpace(runtime.Config.RefreshToken))
        {
            try
            {
                await RefreshServerAsync(runtime.Config.Id, cancellationToken);
            }
            catch
            {
                // Keep config saved even if refresh fails.
            }
        }

        return true;
    }

    /// <summary>
    /// 删除指定的 Agent 服务器配置
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <returns>是否成功删除</returns>
    public bool RemoveServer(string serverId)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        _servers.Remove(runtime);
        Save();
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// 使用用户名和密码登录指定 Agent 服务器
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="username">登录用户名</param>
    /// <param name="password">登录密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>登录是否成功</returns>
    public async Task<bool> LoginAsync(string serverId, string username, string password, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        runtime.State = AgentServerConnectionState.Connecting;
        runtime.LastError = string.Empty;
        NotifyChanged();

        try
        {
            var response = await runtime.Client.LoginAsync(username.Trim(), password, cancellationToken);
            ApplyLoginResponse(runtime, response, username.Trim());
            await SyncServerLocalizationAsync(runtime, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 刷新指定 Agent 服务器的登录态与当前用户信息
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新是否成功</returns>
    public async Task<bool> RefreshServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        runtime.State = AgentServerConnectionState.Connecting;
        runtime.LastError = string.Empty;
        NotifyChanged();

        try
        {
            if (!string.IsNullOrWhiteSpace(runtime.Config.RefreshToken))
            {
                var response = await runtime.Client.RefreshAsync(runtime.Config.RefreshToken, cancellationToken);
                ApplyLoginResponse(runtime, response, runtime.Config.Username);
            }

            if (string.IsNullOrWhiteSpace(runtime.Config.AccessToken))
            {
                runtime.State = AgentServerConnectionState.Unauthorized;
                runtime.LastError = "需要重新登录";
                NotifyChanged();
                Save();
                return false;
            }

            var me = await runtime.Client.GetCurrentUserAsync(runtime.Config.AccessToken, cancellationToken);
            runtime.Config.LastKnownUserName = me.User.Username;
            runtime.LastPermissions = me.Permissions;
            await SyncServerLocalizationAsync(runtime, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (AgentApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            runtime.ClearTokens();
            runtime.State = AgentServerConnectionState.Unauthorized;
            runtime.LastError = runtime.ResolveLocalizedText("Errors.Unauthorized", "登录状态已失效");
            Save();
            NotifyChanged();
            return false;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 退出指定 Agent 服务器的当前登录会话
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>退出是否成功</returns>
    public async Task<bool> LogoutAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(runtime.Config.AccessToken) ||
                !string.IsNullOrWhiteSpace(runtime.Config.RefreshToken))
            {
                await runtime.Client.LogoutAsync(runtime.Config.AccessToken, runtime.Config.RefreshToken, cancellationToken);
            }
        }
        catch
        {
            // 本地退出不应被服务端会话清理失败阻断
        }

        runtime.ClearTokens();
        runtime.State = AgentServerConnectionState.Unauthorized;
        runtime.LastError = string.Empty;
        Save();
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// 退出指定 Agent 服务器当前用户的全部登录会话
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>退出是否成功</returns>
    public async Task<bool> LogoutAllAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            await runtime.Client.LogoutAllAsync(accessToken, cancellationToken);
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }

        runtime.ClearTokens();
        runtime.State = AgentServerConnectionState.Unauthorized;
        runtime.LastError = string.Empty;
        Save();
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// 修改指定 Agent 服务器当前用户密码
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="currentPassword">当前密码</param>
    /// <param name="newPassword">新密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>修改是否成功</returns>
    public async Task<bool> ChangePasswordAsync(string serverId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            await runtime.Client.ChangePasswordAsync(accessToken, currentPassword, newPassword, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器版本信息
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本信息；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentAppVersionResponse?> GetAppVersionAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var version = await runtime.Client.GetAppVersionAsync(runtime.Config.AccessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return version;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器可用语言列表
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可用语言列表；服务器不存在或请求失败时返回空列表</returns>
    public async Task<IReadOnlyList<AgentLanguageOptionDto>> GetLanguagesAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return [];
        }

        try
        {
            var languages = await runtime.Client.GetLanguagesAsync(cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return languages;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return [];
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器语言包内容
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="locale">语言区域代码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>语言包键值集合；服务器不存在或请求失败时返回 null</returns>
    public async Task<Dictionary<string, object>?> GetLocaleAsync(string serverId, string locale, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var data = await runtime.Client.GetLocaleAsync(locale, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return data;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 保存指定 Agent 服务器语言设置
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="locale">语言区域代码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>保存是否成功</returns>
    public async Task<bool> SaveServerLanguageAsync(string serverId, string locale, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var language = await runtime.Client.SaveServerLanguageAsync(accessToken, locale, cancellationToken);
            runtime.Config.AgentLocale = language.Locale.Trim();
            await SyncServerLocalizationAsync(runtime, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器全局 WebRTC 网络设置
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>WebRTC 网络设置；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentWebRtcNetworkSettingsDto?> GetWebRtcNetworkSettingsAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var settings = await runtime.Client.GetWebRtcNetworkSettingsAsync(accessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return settings;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 保存指定 Agent 服务器全局 WebRTC 网络设置
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="settings">WebRTC 网络设置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>保存后的 WebRTC 网络设置；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentWebRtcNetworkSettingsDto?> SaveWebRtcNetworkSettingsAsync(string serverId, AgentWebRtcNetworkSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var savedSettings = await runtime.Client.SaveWebRtcNetworkSettingsAsync(accessToken, settings, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return savedSettings;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器投屏控制可用的 WebRTC 网络设置
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>投屏控制 WebRTC 网络设置；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentWebRtcNetworkSettingsDto?> GetControlWebRtcNetworkSettingsAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var settings = await runtime.Client.GetControlWebRtcNetworkSettingsAsync(accessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return settings;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器账户管理数据
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>账户管理数据；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentAccountDataResponse?> GetAccountDataAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var data = await runtime.Client.GetAccountDataAsync(accessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return data;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 创建指定 Agent 服务器用户
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="request">用户创建请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建后的用户；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentAccountUserDto?> CreateUserAsync(string serverId, AgentUserSaveRequest request, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var user = await runtime.Client.CreateUserAsync(accessToken, request, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return user;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 更新指定 Agent 服务器用户
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="request">用户更新请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的用户；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentAccountUserDto?> UpdateUserAsync(string serverId, int userId, AgentUserSaveRequest request, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var user = await runtime.Client.UpdateUserAsync(accessToken, userId, request, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return user;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 删除指定 Agent 服务器用户
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除是否成功</returns>
    public async Task<bool> DeleteUserAsync(string serverId, int userId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            await runtime.Client.DeleteUserAsync(accessToken, userId, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 设置指定 Agent 服务器用户启用状态
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="isActive">是否启用</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作是否成功</returns>
    public async Task<bool> SetUserActiveAsync(string serverId, int userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            await runtime.Client.SetUserActiveAsync(accessToken, userId, isActive, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 重置指定 Agent 服务器用户密码
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="newPassword">新密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重置密码响应；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentResetPasswordResponse?> ResetUserPasswordAsync(string serverId, int userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var response = await runtime.Client.ResetUserPasswordAsync(accessToken, userId, newPassword, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return response;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器角色管理数据
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>角色与权限数据；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentRolesResponse?> GetRolesAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var roles = await runtime.Client.GetRolesAsync(accessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return roles;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 创建指定 Agent 服务器角色
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="request">角色创建请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建后的角色；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentRoleDto?> CreateRoleAsync(string serverId, AgentRoleSaveRequest request, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var role = await runtime.Client.CreateRoleAsync(accessToken, request, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return role;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 更新指定 Agent 服务器角色
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="roleId">角色 ID</param>
    /// <param name="request">角色更新请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的角色；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentRoleDto?> UpdateRoleAsync(string serverId, int roleId, AgentRoleSaveRequest request, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var role = await runtime.Client.UpdateRoleAsync(accessToken, roleId, request, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return role;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器远程设备剪贴板文本
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剪贴板响应；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentClipboardResponse?> GetClipboardAsync(string serverId, int deviceId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var response = await runtime.Client.GetClipboardAsync(accessToken, deviceId, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return response;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 设置指定 Agent 服务器远程设备剪贴板文本
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="text">剪贴板文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剪贴板响应；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentClipboardResponse?> SetClipboardAsync(string serverId, int deviceId, string text, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var response = await runtime.Client.SetClipboardAsync(accessToken, deviceId, text, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return response;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 设置并粘贴指定 Agent 服务器远程设备剪贴板文本
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="text">剪贴板文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剪贴板响应；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentClipboardResponse?> PasteClipboardAsync(string serverId, int deviceId, string text, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var response = await runtime.Client.PasteClipboardAsync(accessToken, deviceId, text, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return response;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 启动指定 Agent 服务器 ADB 服务
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作是否成功</returns>
    public async Task<bool> StartAdbServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            await runtime.Client.StartAdbServerAsync(accessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 停止指定 Agent 服务器 ADB 服务
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作是否成功</returns>
    public async Task<bool> KillAdbServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            await runtime.Client.KillAdbServerAsync(accessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// 请求指定 Agent 服务器执行 ADB 无线配对
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="host">设备主机地址</param>
    /// <param name="pairingPort">配对端口</param>
    /// <param name="pairingCode">配对码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>配对结果；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentAdbPairResponse?> PairAdbDeviceAsync(string serverId, string host, int pairingPort, string pairingCode, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var response = await runtime.Client.PairAdbDeviceAsync(accessToken, host, pairingPort, pairingCode, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return response;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 获取指定 Agent 服务器 ADB 状态
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>ADB 状态；服务器不存在或请求失败时返回 null</returns>
    public async Task<AgentAdbStatusResponse?> GetAdbStatusAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var runtime = FindServer(serverId);
        if (runtime == null)
        {
            return null;
        }

        try
        {
            var accessToken = await runtime.EnsureAccessTokenAsync(cancellationToken);
            var status = await runtime.Client.GetAdbStatusAsync(accessToken, cancellationToken);
            runtime.TouchSuccess();
            Save();
            NotifyChanged();
            return status;
        }
        catch (Exception ex)
        {
            runtime.State = AgentServerConnectionState.Error;
            runtime.LastError = runtime.ResolveExceptionMessage(ex);
            NotifyChanged();
            return null;
        }
    }

    /// <summary>
    /// 从本地配置中加载所有已保存的服务器
    /// </summary>
    private void Load()
    {
        var configs = _configManager.LoadConfig<List<AgentServerConfig>>(ConfigName) ?? [];
        _servers.Clear();
        foreach (var config in configs)
        {
            var runtime = new AgentServerRuntime(config, Save);
            runtime.State = AgentServerConnectionState.Unknown;
            _servers.Add(runtime);
        }
    }

    /// <summary>
    /// 将当前服务器配置集合持久化到本地配置文件
    /// </summary>
    private void Save()
    {
        _configManager.SaveConfig(ConfigName, _servers.Select(item => item.Config).ToList());
    }

    /// <summary>
    /// 触发服务器集合变化通知
    /// </summary>
    private void NotifyChanged() => ServersChanged?.Invoke();

    /// <summary>
    /// 将登录响应中的令牌和用户信息写回运行时配置
    /// </summary>
    /// <param name="runtime">目标服务器运行时</param>
    /// <param name="response">登录或刷新响应</param>
    /// <param name="username">登录用户名</param>
    private static void ApplyLoginResponse(AgentServerRuntime runtime, AgentLoginResponse response, string username)
    {
        runtime.Config.Username = username;
        runtime.Config.AccessToken = response.AccessToken;
        runtime.Config.AccessTokenExpiresAt = response.AccessTokenExpiresAt;
        runtime.Config.RefreshToken = response.RefreshToken;
        runtime.Config.RefreshTokenExpiresAt = response.RefreshTokenExpiresAt;
        runtime.Config.LastKnownUserName = response.User.Username;
        runtime.LastPermissions = response.Permissions;
    }

    private async Task SyncServerLocalizationAsync(AgentServerRuntime runtime, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(runtime.Config.AccessToken))
            {
                return;
            }

            var language = await runtime.Client.GetServerLanguageAsync(runtime.Config.AccessToken, cancellationToken);
            if (string.IsNullOrWhiteSpace(language.Locale))
            {
                return;
            }

            var payload = await runtime.Client.GetLocaleAsync(language.Locale, cancellationToken);
            runtime.Config.AgentLocale = language.Locale.Trim();
            runtime.Config.AgentTranslations = FlattenTranslations(payload);
        }
        catch
        {
            // 语言缓存同步失败不应影响主流程
        }
    }

    private static Dictionary<string, string> FlattenTranslations(Dictionary<string, object> payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in payload)
        {
            var token = pair.Value switch
            {
                null => JValue.CreateNull(),
                JToken jToken => jToken,
                _ => JToken.FromObject(pair.Value)
            };
            FlattenToken(result, pair.Key, token);
        }

        return result;
    }

    private static void FlattenToken(Dictionary<string, string> target, string prefix, JToken token)
    {
        if (token.Type == JTokenType.Object)
        {
            foreach (var property in token.Children<JProperty>())
            {
                var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                FlattenToken(target, key, property.Value);
            }

            return;
        }

        if (token.Type == JTokenType.String)
        {
            target[prefix] = token.Value<string>() ?? string.Empty;
        }
    }
}

/// <summary>
/// Agent 服务器运行时对象
/// 负责保存单个服务器的当前配置、会话状态与 API 客户端实例
/// </summary>
public sealed class AgentServerRuntime
{
    private AgentApiClient? _client;
    private readonly Action _persistAction;
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);

    /// <summary>
    /// 初始化一个新的 Agent 服务器运行时实例
    /// </summary>
    /// <param name="config">服务器配置</param>
    /// <param name="persistAction">需要持久化配置时调用的回调</param>
    public AgentServerRuntime(AgentServerConfig config, Action persistAction)
    {
        Config = config;
        _persistAction = persistAction;
    }

    /// <summary>
    /// 当前服务器配置
    /// </summary>
    public AgentServerConfig Config { get; }

    /// <summary>
    /// 当前服务器连接状态
    /// </summary>
    public AgentServerConnectionState State { get; set; } = AgentServerConnectionState.Unknown;

    /// <summary>
    /// 最近一次错误消息
    /// </summary>
    public string LastError { get; set; } = string.Empty;

    /// <summary>
    /// 最近一次获取到的权限集合
    /// </summary>
    public IReadOnlyList<string> LastPermissions { get; set; } = [];

    /// <summary>
    /// 延迟初始化的 Agent API 客户端
    /// </summary>
    public AgentApiClient Client => _client ??= new AgentApiClient(Config.BaseUrl);

    /// <summary>
    /// 确保当前运行时拥有可用的访问令牌
    /// 如访问令牌已过期，则会尝试使用刷新令牌换取新令牌
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可用的访问令牌</returns>
    public async Task<string> EnsureAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(Config.AccessToken) &&
            (!Config.AccessTokenExpiresAt.HasValue || Config.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)))
        {
            return Config.AccessToken;
        }

        await _tokenRefreshLock.WaitAsync(cancellationToken);
        try
        {
            // 等待锁期间其他请求可能已经完成刷新，这里再检查一次最新状态。
            if (!string.IsNullOrWhiteSpace(Config.AccessToken) &&
                (!Config.AccessTokenExpiresAt.HasValue || Config.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)))
            {
                return Config.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(Config.RefreshToken))
            {
                throw new InvalidOperationException("服务器未登录。");
            }

            var response = await Client.RefreshAsync(Config.RefreshToken, cancellationToken);
            Config.AccessToken = response.AccessToken;
            Config.AccessTokenExpiresAt = response.AccessTokenExpiresAt;
            Config.RefreshToken = response.RefreshToken;
            Config.RefreshTokenExpiresAt = response.RefreshTokenExpiresAt;
            Config.LastKnownUserName = response.User.Username;
            LastPermissions = response.Permissions;
            _persistAction();
            return Config.AccessToken;
        }
        finally
        {
            _tokenRefreshLock.Release();
        }
    }

    /// <summary>
    /// 记录一次成功访问，并更新运行时状态
    /// </summary>
    public void TouchSuccess()
    {
        Config.LastSyncAt = DateTimeOffset.Now;
        State = AgentServerConnectionState.Connected;
        LastError = string.Empty;
    }

    /// <summary>
    /// 清空当前运行时保存的访问令牌与刷新令牌
    /// </summary>
    public void ClearTokens()
    {
        Config.AccessToken = string.Empty;
        Config.AccessTokenExpiresAt = null;
        Config.RefreshToken = string.Empty;
        Config.RefreshTokenExpiresAt = null;
    }

    /// <summary>
    /// 重置内部 API 客户端实例
    /// 通常在基础地址发生变化后调用
    /// </summary>
    public void ResetClient()
    {
        _client = null;
    }

    /// <summary>
    /// 将 Agent 返回的本地化键转换为适合当前桌面端显示的文本
    /// </summary>
    /// <param name="messageKey">服务端本地化键</param>
    /// <param name="fallbackMessage">回退消息</param>
    /// <returns>可展示给用户的消息</returns>
    public string ResolveLocalizedText(string? messageKey, string? fallbackMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(messageKey))
        {
            if (Config.AgentTranslations.TryGetValue(messageKey, out var remoteText) &&
                !string.IsNullOrWhiteSpace(remoteText))
            {
                return remoteText;
            }

            var localText = LocalizationManager.Instance.GetString(messageKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(localText) && !string.Equals(localText, $"#{messageKey}#", StringComparison.Ordinal))
            {
                return localText;
            }
        }

        return string.IsNullOrWhiteSpace(fallbackMessage)
            ? "操作失败"
            : fallbackMessage;
    }

    /// <summary>
    /// 将异常转换为适合当前桌面端显示的消息
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="fallbackMessage">回退消息</param>
    /// <returns>可展示给用户的异常消息</returns>
    public string ResolveExceptionMessage(Exception exception, string? fallbackMessage = null)
    {
        if (exception is AgentApiException apiException)
        {
            return ResolveLocalizedText(apiException.MessageKey, apiException.Message);
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? ResolveLocalizedText(null, fallbackMessage)
            : exception.Message;
    }
}
