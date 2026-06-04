using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AYLink.Core.Agent;

/// <summary>
/// AYLink.Agent HTTP API 客户端
/// 负责桌面端与远程 Agent 服务端之间的基础请求封装
/// </summary>
public sealed class AgentApiClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化一个新的 Agent API 客户端实例
    /// </summary>
    /// <param name="baseUrl">Agent 服务端的基础地址</param>
    /// <param name="httpClient">可选的外部 HttpClient 实例</param>
    public AgentApiClient(string baseUrl, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be empty.", nameof(baseUrl));
        }

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(NormalizeBaseUrl(baseUrl), UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// 获取 Agent 服务端基础状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端状态响应</returns>
    public Task<AgentStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
        => SendAsync<AgentStatusResponse>(HttpMethod.Get, "/api/status", null, null, cancellationToken);

    /// <summary>
    /// 获取 Agent 应用版本信息
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Agent 应用版本信息</returns>
    public Task<AgentAppVersionResponse> GetAppVersionAsync(string? accessToken = null, CancellationToken cancellationToken = default)
        => SendAsync<AgentAppVersionResponse>(HttpMethod.Get, "/api/app/version", null, accessToken, cancellationToken);

    /// <summary>
    /// 获取 Agent 端 ADB 服务状态
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>ADB 服务状态</returns>
    public Task<AgentAdbStatusResponse> GetAdbStatusAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentAdbStatusResponse>(HttpMethod.Get, "/api/adb/status", null, accessToken, cancellationToken);

    /// <summary>
    /// 启动 Agent 端 ADB 服务
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<AgentSuccessResponse> StartAdbServerAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentSuccessResponse>(HttpMethod.Post, "/api/adb/server/start", null, accessToken, cancellationToken);

    /// <summary>
    /// 停止 Agent 端 ADB 服务
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<AgentSuccessResponse> KillAdbServerAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentSuccessResponse>(HttpMethod.Post, "/api/adb/server/kill", null, accessToken, cancellationToken);

    /// <summary>
    /// 请求 Agent 端执行 ADB 无线配对
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="host">设备主机地址</param>
    /// <param name="pairingPort">配对端口</param>
    /// <param name="pairingCode">配对码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>配对结果</returns>
    public Task<AgentAdbPairResponse> PairAdbDeviceAsync(string accessToken, string host, int pairingPort, string pairingCode, CancellationToken cancellationToken = default)
        => SendAsync<AgentAdbPairResponse>(
            HttpMethod.Post,
            "/api/adb/pair",
            new { host, pairingPort, pairingCode },
            accessToken,
            cancellationToken);

    /// <summary>
    /// 使用用户名和密码登录 Agent 服务端
    /// </summary>
    /// <param name="username">登录用户名</param>
    /// <param name="password">登录密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>登录响应</returns>
    public Task<AgentLoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
        => SendAsync<AgentLoginResponse>(
            HttpMethod.Post,
            "/api/login",
            new { username, password },
            null,
            cancellationToken);

    /// <summary>
    /// 使用刷新令牌换取新的访问令牌
    /// </summary>
    /// <param name="refreshToken">刷新令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新后的登录响应</returns>
    public Task<AgentLoginResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentLoginResponse>(
            HttpMethod.Post,
            "/api/auth/refresh",
            new { refreshToken },
            null,
            cancellationToken);

    /// <summary>
    /// 获取当前登录用户信息
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前用户信息响应</returns>
    public Task<AgentMeResponse> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentMeResponse>(HttpMethod.Get, "/api/auth/me", null, accessToken, cancellationToken);

    /// <summary>
    /// 退出当前 Agent 登录会话
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="refreshToken">可选的刷新令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<AgentSuccessResponse> LogoutAsync(string accessToken, string? refreshToken = null, CancellationToken cancellationToken = default)
        => SendAsync<AgentSuccessResponse>(HttpMethod.Post, "/api/logout", new { refreshToken }, accessToken, cancellationToken);

    /// <summary>
    /// 退出当前用户在 Agent 服务端上的全部登录会话
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<AgentSuccessResponse> LogoutAllAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentSuccessResponse>(HttpMethod.Post, "/api/logout-all", null, accessToken, cancellationToken);

    /// <summary>
    /// 修改当前 Agent 登录用户密码
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="currentPassword">当前密码</param>
    /// <param name="newPassword">新密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<AgentSuccessResponse> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        => SendAsync<AgentSuccessResponse>(
            HttpMethod.Post,
            "/api/auth/change-password",
            new { currentPassword, newPassword },
            accessToken,
            cancellationToken);

    /// <summary>
    /// 获取 Agent 账户管理数据
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>账户、角色与可用权限列表</returns>
    public Task<AgentAccountDataResponse> GetAccountDataAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentAccountDataResponse>(HttpMethod.Get, "/api/accounts/users", null, accessToken, cancellationToken);

    /// <summary>
    /// 创建 Agent 用户
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="request">用户创建请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建后的用户</returns>
    public async Task<AgentAccountUserDto> CreateUserAsync(string accessToken, AgentUserSaveRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentUserSaveResponse>(HttpMethod.Post, "/api/accounts/users", request, accessToken, cancellationToken);
        return response.User;
    }

    /// <summary>
    /// 更新 Agent 用户
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="request">用户更新请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的用户</returns>
    public async Task<AgentAccountUserDto> UpdateUserAsync(string accessToken, int userId, AgentUserSaveRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentUserSaveResponse>(HttpMethod.Put, $"/api/accounts/users/{userId}", request, accessToken, cancellationToken);
        return response.User;
    }

    /// <summary>
    /// 删除 Agent 用户
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除是否成功</returns>
    public async Task<bool> DeleteUserAsync(string accessToken, int userId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete, $"/api/accounts/users/{userId}", null, accessToken, cancellationToken);
        return true;
    }

    /// <summary>
    /// 设置 Agent 用户启用状态
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="isActive">是否启用</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<AgentSuccessResponse> SetUserActiveAsync(string accessToken, int userId, bool isActive, CancellationToken cancellationToken = default)
        => SendAsync<AgentSuccessResponse>(
            HttpMethod.Post,
            $"/api/accounts/users/{userId}/{(isActive ? "activate" : "deactivate")}",
            null,
            accessToken,
            cancellationToken);

    /// <summary>
    /// 重置 Agent 用户密码
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="newPassword">新密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重置后的密码响应</returns>
    public Task<AgentResetPasswordResponse> ResetUserPasswordAsync(string accessToken, int userId, string newPassword, CancellationToken cancellationToken = default)
        => SendAsync<AgentResetPasswordResponse>(
            HttpMethod.Post,
            $"/api/accounts/users/{userId}/reset-password",
            new { newPassword },
            accessToken,
            cancellationToken);

    /// <summary>
    /// 获取 Agent 角色与可用权限列表
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>角色与可用权限列表</returns>
    public Task<AgentRolesResponse> GetRolesAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentRolesResponse>(HttpMethod.Get, "/api/accounts/roles", null, accessToken, cancellationToken);

    /// <summary>
    /// 创建 Agent 角色
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="request">角色创建请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建后的角色</returns>
    public async Task<AgentRoleDto> CreateRoleAsync(string accessToken, AgentRoleSaveRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentRoleSaveResponse>(HttpMethod.Post, "/api/accounts/roles", request, accessToken, cancellationToken);
        return response.Role;
    }

    /// <summary>
    /// 更新 Agent 角色
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="roleId">角色 ID</param>
    /// <param name="request">角色更新请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的角色</returns>
    public async Task<AgentRoleDto> UpdateRoleAsync(string accessToken, int roleId, AgentRoleSaveRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentRoleSaveResponse>(HttpMethod.Put, $"/api/accounts/roles/{roleId}", request, accessToken, cancellationToken);
        return response.Role;
    }

    /// <summary>
    /// 获取 Agent 服务端可见的设备列表
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>远程设备列表</returns>
    public Task<IReadOnlyList<AgentDeviceDto>> GetDevicesAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<AgentDeviceDto>>(HttpMethod.Get, "/api/devices", null, accessToken, cancellationToken);

    /// <summary>
    /// 向 Agent 服务端提交新增设备请求
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="request">新增设备请求体</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新增后的远程设备信息</returns>
    public Task<AgentDeviceDto> AddDeviceAsync(string accessToken, AgentCreateDeviceRequest request, CancellationToken cancellationToken = default)
        => SendAsync<AgentDeviceDto>(HttpMethod.Post, "/api/devices", request, accessToken, cancellationToken);

    /// <summary>
    /// 请求 Agent 服务端连接指定远程设备
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接后的远程设备信息</returns>
    public Task<AgentDeviceDto> ConnectDeviceAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
        => SendAsync<AgentDeviceDto>(HttpMethod.Post, $"/api/devices/connect/{deviceId}", null, accessToken, cancellationToken);

    /// <summary>
    /// 请求 Agent 服务端重命名指定远程设备
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="name">新的设备名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的远程设备信息</returns>
    public Task<AgentDeviceDto> RenameDeviceAsync(string accessToken, int deviceId, string name, CancellationToken cancellationToken = default)
        => SendAsync<AgentDeviceDto>(HttpMethod.Put, $"/api/devices/{deviceId}/rename", new { Name = name }, accessToken, cancellationToken);

    /// <summary>
    /// 获取远程设备预览图片
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="width">预览图片目标宽度</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>预览图片下载响应</returns>
    public Task<AgentDownloadResponse> DownloadDevicePreviewAsync(string accessToken, int deviceId, int? width = null, CancellationToken cancellationToken = default)
    {
        var path = $"/api/devices/{deviceId}/preview";
        if (width.HasValue && width.Value > 0)
        {
            path += $"?width={width.Value}";
        }

        return SendDownloadAsync(HttpMethod.Get, path, null, accessToken, cancellationToken);
    }

    /// <summary>
    /// 获取 Agent 服务端的设备分组列表
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="keyword">可选的分组名称筛选关键字</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备分组列表</returns>
    public async Task<IReadOnlyList<AgentDeviceGroupDto>> GetDeviceGroupsAsync(string accessToken, string? keyword = null, CancellationToken cancellationToken = default)
    {
        var path = "/api/device-groups";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            path += $"?keyword={Uri.EscapeDataString(keyword.Trim())}";
        }

        var response = await SendAsync<AgentDeviceGroupListResponse>(HttpMethod.Get, path, null, accessToken, cancellationToken);
        return response.Items;
    }

    /// <summary>
    /// 获取当前用户可选择的 Agent 设备分组选项
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="keyword">可选的分组名称筛选关键字</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备分组选项列表</returns>
    public async Task<IReadOnlyList<AgentDeviceGroupDto>> GetDeviceGroupOptionsAsync(string accessToken, string? keyword = null, CancellationToken cancellationToken = default)
    {
        var path = "/api/device-groups/options";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            path += $"?keyword={Uri.EscapeDataString(keyword.Trim())}";
        }

        var response = await SendAsync<AgentDeviceGroupListResponse>(HttpMethod.Get, path, null, accessToken, cancellationToken);
        return response.Items;
    }

    /// <summary>
    /// 创建 Agent 服务端设备分组
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="name">分组名称</param>
    /// <param name="description">分组描述</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建后的设备分组</returns>
    public async Task<AgentDeviceGroupDto> CreateDeviceGroupAsync(string accessToken, string name, string description, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentDeviceGroupSaveResponse>(
            HttpMethod.Post,
            "/api/device-groups",
            new { name, description },
            accessToken,
            cancellationToken);
        return response.Group;
    }

    /// <summary>
    /// 更新 Agent 服务端设备分组
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="groupId">服务端分组 ID</param>
    /// <param name="name">分组名称</param>
    /// <param name="description">分组描述</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的设备分组</returns>
    public async Task<AgentDeviceGroupDto> UpdateDeviceGroupAsync(string accessToken, int groupId, string name, string description, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentDeviceGroupSaveResponse>(
            HttpMethod.Put,
            $"/api/device-groups/{groupId}",
            new { name, description },
            accessToken,
            cancellationToken);
        return response.Group;
    }

    /// <summary>
    /// 删除 Agent 服务端设备分组
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="groupId">服务端分组 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除是否成功</returns>
    public async Task<bool> DeleteDeviceGroupAsync(string accessToken, int groupId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete, $"/api/device-groups/{groupId}", null, accessToken, cancellationToken);
        return true;
    }

    /// <summary>
    /// 获取指定远程设备所属的 Agent 设备分组
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备所属分组列表</returns>
    public async Task<IReadOnlyList<AgentDeviceGroupDto>> GetDeviceGroupsForDeviceAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentDeviceGroupsResponse>(HttpMethod.Get, $"/api/devices/{deviceId}/groups", null, accessToken, cancellationToken);
        return response.Groups;
    }

    /// <summary>
    /// 保存指定远程设备所属的 Agent 设备分组
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="groupIds">目标服务端分组 ID 集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>保存后的设备所属分组列表</returns>
    public async Task<IReadOnlyList<AgentDeviceGroupDto>> SaveDeviceGroupsForDeviceAsync(string accessToken, int deviceId, IEnumerable<int> groupIds, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AgentDeviceGroupsResponse>(
            HttpMethod.Put,
            $"/api/devices/{deviceId}/groups",
            new { groupIds = groupIds.Distinct().ToList() },
            accessToken,
            cancellationToken);
        return response.Groups;
    }

    /// <summary>
    /// 请求 Agent 服务端删除指定远程设备
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除是否成功</returns>
    public async Task<bool> DeleteDeviceAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/devices/{deviceId}");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var content = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        Debug.WriteLine($"[AgentApi] DELETE /api/devices/{deviceId} failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={content}");
        throw CreateAgentApiException(response.StatusCode, response.ReasonPhrase, content);
    }

    /// <summary>
    /// 获取指定远程设备的设置
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备设置</returns>
    public Task<AgentDeviceSettingsDto> GetDeviceSettingsAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
        => SendAsync<AgentDeviceSettingsDto>(HttpMethod.Get, $"/api/devices/{deviceId}/settings", null, accessToken, cancellationToken);

    /// <summary>
    /// 保存指定远程设备的设置
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="settings">设备设置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端规范化后的设备设置</returns>
    public Task<AgentDeviceSettingsDto> SaveDeviceSettingsAsync(string accessToken, int deviceId, AgentDeviceSettingsDto settings, CancellationToken cancellationToken = default)
        => SendAsync<AgentDeviceSettingsDto>(HttpMethod.Put, $"/api/devices/{deviceId}/settings", settings, accessToken, cancellationToken);

    /// <summary>
    /// 将指定远程设备设置恢复为默认值
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>恢复后的默认设备设置</returns>
    public Task<AgentDeviceSettingsDto> ResetDeviceSettingsAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
        => SendAsync<AgentDeviceSettingsDto>(HttpMethod.Delete, $"/api/devices/{deviceId}/settings", null, accessToken, cancellationToken);

    /// <summary>
    /// 获取指定远程设备的应用列表
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>应用列表</returns>
    public Task<IReadOnlyList<AgentAppDto>> GetAppsAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<AgentAppDto>>(HttpMethod.Get, $"/api/devices/{deviceId}/apps", null, accessToken, cancellationToken);

    /// <summary>
    /// 获取指定远程设备的编码器列表
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>编码器列表</returns>
    public Task<IReadOnlyList<string>> GetEncodersAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<string>>(HttpMethod.Get, $"/api/devices/{deviceId}/encoders", null, accessToken, cancellationToken);

    /// <summary>
    /// 请求远程启动指定应用
    /// </summary>
    public Task<AgentBooleanResponse> LaunchAppAsync(string accessToken, int deviceId, string packageName, CancellationToken cancellationToken = default)
        => SendAsync<AgentBooleanResponse>(HttpMethod.Post, $"/api/devices/{deviceId}/apps/launch", new { packageName }, accessToken, cancellationToken);

    /// <summary>
    /// 请求远程卸载指定应用
    /// </summary>
    public Task<AgentBooleanResponse> UninstallAppAsync(string accessToken, int deviceId, string packageName, CancellationToken cancellationToken = default)
        => SendAsync<AgentBooleanResponse>(HttpMethod.Post, $"/api/devices/{deviceId}/apps/uninstall", new { packageName }, accessToken, cancellationToken);

    /// <summary>
    /// 获取指定应用的详细信息
    /// </summary>
    public Task<AgentAppInfoDto> GetAppInfoAsync(string accessToken, int deviceId, string packageName, CancellationToken cancellationToken = default)
        => SendAsync<AgentAppInfoDto>(HttpMethod.Post, $"/api/devices/{deviceId}/apps/info", new { packageName }, accessToken, cancellationToken);

    /// <summary>
    /// 下载指定应用的 APK 文件
    /// </summary>
    public Task<AgentDownloadResponse> DownloadAppAsync(string accessToken, int deviceId, string packageName, CancellationToken cancellationToken = default)
        => SendDownloadAsync(HttpMethod.Post, $"/api/devices/{deviceId}/apps/download", new { packageName }, accessToken, cancellationToken);

    /// <summary>
    /// 上传并安装 APK 文件
    /// </summary>
    public async Task<AgentBooleanResponse> InstallAppAsync(string accessToken, int deviceId, string fileName, Stream fileStream, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        form.Add(streamContent, "file", fileName);
        return await SendMultipartAsync<AgentBooleanResponse>(HttpMethod.Post, $"/api/devices/{deviceId}/apps/install", form, accessToken, cancellationToken);
    }

    /// <summary>
    /// 获取指定目录下的文件列表
    /// </summary>
    public Task<AgentFileListResponse> ListFilesAsync(string accessToken, int deviceId, string path, CancellationToken cancellationToken = default)
        => SendAsync<AgentFileListResponse>(HttpMethod.Post, $"/api/devices/{deviceId}/files/list", new { path }, accessToken, cancellationToken);

    /// <summary>
    /// 下载指定文件
    /// </summary>
    public Task<AgentDownloadResponse> DownloadFileAsync(string accessToken, int deviceId, string path, CancellationToken cancellationToken = default)
        => SendDownloadAsync(HttpMethod.Post, $"/api/devices/{deviceId}/files/download", new { path }, accessToken, cancellationToken);

    /// <summary>
    /// 重命名指定文件或目录
    /// </summary>
    public Task<AgentBooleanResponse> RenameFileAsync(string accessToken, int deviceId, string path, string newName, CancellationToken cancellationToken = default)
        => SendAsync<AgentBooleanResponse>(HttpMethod.Post, $"/api/devices/{deviceId}/files/rename", new { path, newName }, accessToken, cancellationToken);

    /// <summary>
    /// 删除指定文件或目录
    /// </summary>
    public Task<AgentBooleanResponse> DeleteFileAsync(string accessToken, int deviceId, string path, CancellationToken cancellationToken = default)
        => SendAsync<AgentBooleanResponse>(HttpMethod.Post, $"/api/devices/{deviceId}/files/delete", new { path }, accessToken, cancellationToken);

    /// <summary>
    /// 读取远程设备剪贴板
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剪贴板内容响应</returns>
    public Task<AgentClipboardResponse> GetClipboardAsync(string accessToken, int deviceId, CancellationToken cancellationToken = default)
        => SendAsync<AgentClipboardResponse>(HttpMethod.Get, $"/api/devices/{deviceId}/clipboard", null, accessToken, cancellationToken);

    /// <summary>
    /// 同步写入远程设备剪贴板
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="text">剪贴板文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剪贴板写入响应</returns>
    public Task<AgentClipboardResponse> SetClipboardAsync(string accessToken, int deviceId, string text, CancellationToken cancellationToken = default)
        => SendAsync<AgentClipboardResponse>(HttpMethod.Put, $"/api/devices/{deviceId}/clipboard", new { text }, accessToken, cancellationToken);

    /// <summary>
    /// 请求远程设备粘贴给定文本
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <param name="text">待粘贴文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剪贴板粘贴响应</returns>
    public Task<AgentClipboardResponse> PasteClipboardAsync(string accessToken, int deviceId, string text, CancellationToken cancellationToken = default)
        => SendAsync<AgentClipboardResponse>(HttpMethod.Post, $"/api/devices/{deviceId}/clipboard", new { text }, accessToken, cancellationToken);

    /// <summary>
    /// 获取 Agent 服务端的全局 WebRTC 网络设置
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>WebRTC 网络设置</returns>
    public Task<AgentWebRtcNetworkSettingsDto> GetWebRtcNetworkSettingsAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentWebRtcNetworkSettingsDto>(HttpMethod.Get, "/api/settings/webrtc-network", null, accessToken, cancellationToken);

    /// <summary>
    /// 获取 Agent 服务端当前语言设置
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端当前语言</returns>
    public Task<AgentLanguageSettingResponse> GetServerLanguageAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentLanguageSettingResponse>(HttpMethod.Get, "/api/settings/language", null, accessToken, cancellationToken);

    /// <summary>
    /// 保存 Agent 服务端当前语言设置
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="locale">语言区域代码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端保存后的语言设置</returns>
    public Task<AgentLanguageSettingResponse> SaveServerLanguageAsync(string accessToken, string locale, CancellationToken cancellationToken = default)
        => SendAsync<AgentLanguageSettingResponse>(HttpMethod.Put, "/api/settings/language", new { locale }, accessToken, cancellationToken);

    /// <summary>
    /// 获取 Agent 服务端可用语言列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可用语言列表</returns>
    public Task<IReadOnlyList<AgentLanguageOptionDto>> GetLanguagesAsync(CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<AgentLanguageOptionDto>>(HttpMethod.Get, "/api/i18n/languages", null, null, cancellationToken);

    /// <summary>
    /// 获取 Agent 服务端指定语言包
    /// </summary>
    /// <param name="locale">语言区域代码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>语言包内容</returns>
    public Task<Dictionary<string, object>> GetLocaleAsync(string locale, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("Locale cannot be empty.", nameof(locale));
        }

        return SendAsync<Dictionary<string, object>>(HttpMethod.Get, $"/api/i18n/{Uri.EscapeDataString(locale.Trim())}", null, null, cancellationToken);
    }

    /// <summary>
    /// 保存 Agent 服务端的全局 WebRTC 网络设置
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="settings">待保存的 WebRTC 网络设置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端规范化后的 WebRTC 网络设置</returns>
    public Task<AgentWebRtcNetworkSettingsDto> SaveWebRtcNetworkSettingsAsync(string accessToken, AgentWebRtcNetworkSettingsDto settings, CancellationToken cancellationToken = default)
        => SendAsync<AgentWebRtcNetworkSettingsDto>(HttpMethod.Put, "/api/settings/webrtc-network", settings, accessToken, cancellationToken);

    /// <summary>
    /// 创建一个新的 Agent WebRTC 投屏票据
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="request">票据创建参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话票据响应</returns>
    public Task<AgentWebRtcTicketResponse> CreateWebRtcTicketAsync(string accessToken, AgentWebRtcTicketRequest request, CancellationToken cancellationToken = default)
        => SendAsync<AgentWebRtcTicketResponse>(HttpMethod.Post, "/api/webrtc-ticket", request, accessToken, cancellationToken);

    /// <summary>
    /// 获取设备控制权限可访问的 WebRTC 网络设置
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>WebRTC 网络设置</returns>
    public Task<AgentWebRtcNetworkSettingsDto> GetControlWebRtcNetworkSettingsAsync(string accessToken, CancellationToken cancellationToken = default)
        => SendAsync<AgentWebRtcNetworkSettingsDto>(HttpMethod.Get, "/api/control/webrtc-network", null, accessToken, cancellationToken);

    /// <summary>
    /// 向 Agent 汇报当前 scrcpy 会话仍然存活
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备标识</param>
    /// <param name="sessionId">当前会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话保活结果</returns>
    public Task<AgentBooleanResponse> TouchScrcpySessionAsync(string accessToken, string deviceId, string sessionId, CancellationToken cancellationToken = default)
        => SendAsync<AgentBooleanResponse>(
            HttpMethod.Post,
            "/api/scrcpy-sessions/heartbeat",
            new AgentScrcpySessionActionRequest { DeviceId = deviceId, SessionId = sessionId },
            accessToken,
            cancellationToken);

    /// <summary>
    /// 通知 Agent 主动释放当前 scrcpy 会话
    /// </summary>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="deviceId">服务端设备标识</param>
    /// <param name="sessionId">当前会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话释放结果</returns>
    public Task<AgentBooleanResponse> ReleaseScrcpySessionAsync(string accessToken, string deviceId, string sessionId, CancellationToken cancellationToken = default)
        => SendAsync<AgentBooleanResponse>(
            HttpMethod.Post,
            "/api/scrcpy-sessions/release",
            new AgentScrcpySessionActionRequest { DeviceId = deviceId, SessionId = sessionId },
            accessToken,
            cancellationToken);

    /// <summary>
    /// 构造当前 Agent 的 WebRTC 信令 WebSocket 地址
    /// </summary>
    /// <param name="ticket">已签发的会话票据</param>
    /// <returns>WebSocket 连接地址</returns>
    public Uri BuildWebRtcSignalUri(string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            throw new ArgumentException("Ticket cannot be empty.", nameof(ticket));
        }

        var baseUri = _httpClient.BaseAddress ?? throw new InvalidOperationException("BaseAddress is not configured.");
        var builder = new UriBuilder(baseUri)
        {
            Scheme = string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/webrtc",
            Query = $"ticket={Uri.EscapeDataString(ticket.Trim())}"
        };

        return builder.Uri;
    }

    /// <summary>
    /// 构造当前 Agent 的终端 WebSocket 地址
    /// </summary>
    /// <param name="deviceId">服务端设备 ID</param>
    /// <returns>WebSocket 连接地址</returns>
    public Uri BuildTerminalWebSocketUri(int deviceId)
    {
        var baseUri = _httpClient.BaseAddress ?? throw new InvalidOperationException("BaseAddress is not configured.");
        return new UriBuilder(baseUri)
        {
            Scheme = string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = $"/api/devices/{deviceId}/terminal/ws",
            Query = string.Empty
        }.Uri;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? payload,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (payload != null)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[AgentApi] {method} {path} failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={content}");
            throw CreateAgentApiException(response.StatusCode, response.ReasonPhrase, content);
        }

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        return JsonConvert.DeserializeObject<T>(content)
            ?? throw new AgentApiException(HttpStatusCode.InternalServerError, "Unable to parse agent response.");
    }

    private async Task<T> SendMultipartAsync<T>(
        HttpMethod method,
        string path,
        HttpContent content,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[AgentApi] {method} {path} failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={body}");
            throw CreateAgentApiException(response.StatusCode, response.ReasonPhrase, body);
        }

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(body))
        {
            return default!;
        }

        return JsonConvert.DeserializeObject<T>(body)
            ?? throw new AgentApiException(HttpStatusCode.InternalServerError, "Unable to parse agent response.");
    }

    private async Task<AgentDownloadResponse> SendDownloadAsync(
        HttpMethod method,
        string path,
        object? payload,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (payload != null)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json");
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            Debug.WriteLine($"[AgentApi] {method} {path} failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={body}");
            throw CreateAgentApiException(response.StatusCode, response.ReasonPhrase, body);
        }

        return new AgentDownloadResponse(response);
    }

    private static string NormalizeBaseUrl(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = $"http://{trimmed}";
        }

        return trimmed.TrimEnd('/');
    }

    private static AgentApiException CreateAgentApiException(HttpStatusCode statusCode, string? reasonPhrase, string content)
    {
        var error = ParseError(content);
        return new AgentApiException(
            statusCode,
            error?.Message ?? reasonPhrase ?? "Request failed.",
            error?.MessageKey,
            error?.Code);
    }

    private static AgentErrorPayload? ParseError(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            var error = JsonConvert.DeserializeObject<AgentErrorResponse>(content);
            return error?.Error;
        }
        catch
        {
            return new AgentErrorPayload
            {
                Message = content
            };
        }
    }
}

/// <summary>
/// Agent API 请求失败时抛出的异常
/// </summary>
/// <param name="statusCode">HTTP 状态码</param>
/// <param name="message">错误消息</param>
/// <param name="messageKey">服务端返回的本地化键</param>
/// <param name="errorCode">服务端返回的错误代码</param>
public sealed class AgentApiException(HttpStatusCode statusCode, string message, string? messageKey = null, string? errorCode = null) : Exception(message)
{
    /// <summary>
    /// 失败请求对应的 HTTP 状态码
    /// </summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>
    /// 服务端返回的本地化键
    /// </summary>
    public string? MessageKey { get; } = messageKey;

    /// <summary>
    /// 服务端返回的错误代码
    /// </summary>
    public string? ErrorCode { get; } = errorCode;
}

/// <summary>
/// Agent 服务端状态响应模型
/// </summary>
public sealed class AgentStatusResponse
{
    /// <summary>
    /// 服务端状态文本
    /// </summary>
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 服务端运行模式
    /// </summary>
    [JsonProperty("Mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// 服务端状态时间戳
    /// </summary>
    [JsonProperty("Timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Agent 端 ADB 状态摘要
    /// </summary>
    [JsonProperty("adb")]
    public AgentAdbSummaryDto? Adb { get; set; }

    /// <summary>
    /// Agent 端当前 ADB 设备列表
    /// </summary>
    [JsonProperty("devices")]
    public List<AgentAdbDeviceDto> Devices { get; set; } = [];
}

/// <summary>
/// Agent 应用版本响应模型
/// </summary>
public sealed class AgentAppVersionResponse
{
    /// <summary>
    /// Agent 后端版本
    /// </summary>
    [JsonProperty("agentVersion")]
    public string AgentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Agent Web 前端版本
    /// </summary>
    [JsonProperty("webVersion")]
    public string WebVersion { get; set; } = string.Empty;

    /// <summary>
    /// 对外展示版本
    /// </summary>
    [JsonProperty("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 发布标签
    /// </summary>
    [JsonProperty("releaseTag")]
    public string ReleaseTag { get; set; } = string.Empty;

    /// <summary>
    /// 仓库地址
    /// </summary>
    [JsonProperty("repositoryUrl")]
    public string RepositoryUrl { get; set; } = string.Empty;

    /// <summary>
    /// 最新版本发布页
    /// </summary>
    [JsonProperty("latestReleaseUrl")]
    public string LatestReleaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Agent ADB 状态响应模型
/// </summary>
public sealed class AgentAdbStatusResponse
{
    /// <summary>
    /// ADB 服务地址
    /// </summary>
    [JsonProperty("serverAddress")]
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>
    /// 已解析的 ADB 可执行文件
    /// </summary>
    [JsonProperty("binary")]
    public AgentAdbBinaryDto? Binary { get; set; }

    /// <summary>
    /// 当前 ADB 设备列表
    /// </summary>
    [JsonProperty("devices")]
    public List<AgentAdbDeviceDto> Devices { get; set; } = [];
}

/// <summary>
/// Agent ADB 摘要模型
/// </summary>
public sealed class AgentAdbSummaryDto
{
    /// <summary>
    /// ADB 服务地址
    /// </summary>
    [JsonProperty("serverAddress")]
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>
    /// ADB 可执行文件路径
    /// </summary>
    [JsonProperty("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// ADB 可执行文件来源
    /// </summary>
    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Agent ADB 可执行文件模型
/// </summary>
public sealed class AgentAdbBinaryDto
{
    /// <summary>
    /// ADB 可执行文件路径
    /// </summary>
    [JsonProperty("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// ADB 可执行文件来源
    /// </summary>
    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Agent ADB 设备摘要模型
/// </summary>
public sealed class AgentAdbDeviceDto
{
    /// <summary>
    /// 设备序列号
    /// </summary>
    [JsonProperty("serial")]
    public string Serial { get; set; } = string.Empty;

    /// <summary>
    /// 设备连接状态
    /// </summary>
    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// Agent ADB 配对响应模型
/// </summary>
public sealed class AgentAdbPairResponse
{
    /// <summary>
    /// 操作是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 配对主机地址
    /// </summary>
    [JsonProperty("host")]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 配对端口
    /// </summary>
    [JsonProperty("pairingPort")]
    public int PairingPort { get; set; }

    /// <summary>
    /// 失败错误信息
    /// </summary>
    [JsonProperty("error")]
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Agent 通用成功响应模型
/// </summary>
public sealed class AgentSuccessResponse
{
    /// <summary>
    /// 当前请求是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }
}

/// <summary>
/// Agent 布尔值结果响应模型
/// </summary>
public sealed class AgentBooleanResponse
{
    /// <summary>
    /// 当前请求是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 兼容部分设备接口返回的 ok 字段
    /// </summary>
    [JsonProperty("ok")]
    public bool Ok
    {
        get => Success;
        set => Success = value;
    }
}

/// <summary>
/// Agent 登录与刷新令牌响应模型
/// </summary>
public sealed class AgentLoginResponse
{
    /// <summary>
    /// 当前请求是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 访问令牌
    /// </summary>
    [JsonProperty("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 访问令牌过期时间
    /// </summary>
    [JsonProperty("accessTokenExpiresAt")]
    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    [JsonProperty("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// 刷新令牌过期时间
    /// </summary>
    [JsonProperty("refreshTokenExpiresAt")]
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }

    /// <summary>
    /// 当前登录用户信息
    /// </summary>
    [JsonProperty("user")]
    public AgentUserDto User { get; set; } = new();

    /// <summary>
    /// 当前用户拥有的权限集合
    /// </summary>
    [JsonProperty("permissions")]
    public List<string> Permissions { get; set; } = [];
}

/// <summary>
/// 获取当前用户信息接口的响应模型
/// </summary>
public sealed class AgentMeResponse
{
    /// <summary>
    /// 当前登录用户信息
    /// </summary>
    [JsonProperty("user")]
    public AgentUserDto User { get; set; } = new();

    /// <summary>
    /// 当前用户拥有的权限集合
    /// </summary>
    [JsonProperty("permissions")]
    public List<string> Permissions { get; set; } = [];
}

/// <summary>
/// Agent 用户信息模型
/// </summary>
public sealed class AgentUserDto
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    [JsonProperty(nameof(Id))]
    public int Id { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    [JsonProperty(nameof(Username))]
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// Agent 账户管理数据响应模型
/// </summary>
public sealed class AgentAccountDataResponse
{
    /// <summary>
    /// 用户列表
    /// </summary>
    [JsonProperty("users")]
    public List<AgentAccountUserDto> Users { get; set; } = [];

    /// <summary>
    /// 角色列表
    /// </summary>
    [JsonProperty("roles")]
    public List<AgentRoleDto> Roles { get; set; } = [];

    /// <summary>
    /// 可用权限列表
    /// </summary>
    [JsonProperty("availablePermissions")]
    public List<AgentPermissionDto> AvailablePermissions { get; set; } = [];
}

/// <summary>
/// Agent 角色列表响应模型
/// </summary>
public sealed class AgentRolesResponse
{
    /// <summary>
    /// 角色列表
    /// </summary>
    [JsonProperty("roles")]
    public List<AgentRoleDto> Roles { get; set; } = [];

    /// <summary>
    /// 可用权限列表
    /// </summary>
    [JsonProperty("availablePermissions")]
    public List<AgentPermissionDto> AvailablePermissions { get; set; } = [];
}

/// <summary>
/// Agent 账户用户模型
/// </summary>
public sealed class AgentAccountUserDto
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    [JsonProperty(nameof(Id))]
    public int Id { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    [JsonProperty(nameof(Username))]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    [JsonProperty(nameof(IsActive))]
    public bool IsActive { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [JsonProperty(nameof(CreatedAt))]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    [JsonProperty(nameof(UpdatedAt))]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 最近登录时间
    /// </summary>
    [JsonProperty(nameof(LastLoginAt))]
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// 直接分配角色
    /// </summary>
    [JsonProperty(nameof(Roles))]
    public List<AgentRoleSummaryDto> Roles { get; set; } = [];

    /// <summary>
    /// 权限集合
    /// </summary>
    [JsonProperty(nameof(Permissions))]
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// 用户直接绑定的设备分组
    /// </summary>
    [JsonProperty(nameof(DirectDeviceGroups))]
    public List<AgentDeviceGroupDto> DirectDeviceGroups { get; set; } = [];

    /// <summary>
    /// 用户最终可访问的设备分组
    /// </summary>
    [JsonProperty(nameof(EffectiveDeviceGroups))]
    public List<AgentDeviceGroupDto> EffectiveDeviceGroups { get; set; } = [];

    /// <summary>
    /// 用户最终可访问设备数量
    /// </summary>
    [JsonProperty(nameof(EffectiveDeviceCount))]
    public int EffectiveDeviceCount { get; set; }

    /// <summary>
    /// 用户最终可访问设备分组数量
    /// </summary>
    [JsonProperty(nameof(EffectiveDeviceGroupCount))]
    public int EffectiveDeviceGroupCount { get; set; }
}

/// <summary>
/// Agent 用户保存请求模型
/// </summary>
public sealed class AgentUserSaveRequest
{
    /// <summary>
    /// 用户名
    /// </summary>
    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 新建用户密码；更新用户时通常留空
    /// </summary>
    [JsonProperty("password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    [JsonProperty("isActive")]
    public bool? IsActive { get; set; }

    /// <summary>
    /// 角色 ID 集合
    /// </summary>
    [JsonProperty("roleIds")]
    public List<int> RoleIds { get; set; } = [];

    /// <summary>
    /// 直接绑定的设备分组 ID 集合
    /// </summary>
    [JsonProperty("deviceGroupIds")]
    public List<int> DeviceGroupIds { get; set; } = [];
}

/// <summary>
/// Agent 用户保存响应模型
/// </summary>
internal sealed class AgentUserSaveResponse
{
    /// <summary>
    /// 保存是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 保存后的用户
    /// </summary>
    [JsonProperty("user")]
    public AgentAccountUserDto User { get; set; } = new();
}

/// <summary>
/// Agent 重置密码响应模型
/// </summary>
public sealed class AgentResetPasswordResponse
{
    /// <summary>
    /// 重置是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 重置后的密码
    /// </summary>
    [JsonProperty("password")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Agent 角色摘要模型
/// </summary>
public sealed class AgentRoleSummaryDto
{
    /// <summary>
    /// 角色 ID
    /// </summary>
    [JsonProperty(nameof(Id))]
    public int Id { get; set; }

    /// <summary>
    /// 角色名称
    /// </summary>
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [JsonProperty(nameof(Description))]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Agent 角色模型
/// </summary>
public sealed class AgentRoleDto
{
    /// <summary>
    /// 角色 ID
    /// </summary>
    [JsonProperty(nameof(Id))]
    public int Id { get; set; }

    /// <summary>
    /// 角色名称
    /// </summary>
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [JsonProperty(nameof(Description))]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否为内置角色
    /// </summary>
    [JsonProperty(nameof(IsInternal))]
    public bool IsInternal { get; set; }

    /// <summary>
    /// 权限集合
    /// </summary>
    [JsonProperty(nameof(Permissions))]
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// 角色绑定的设备分组
    /// </summary>
    [JsonProperty(nameof(DeviceGroups))]
    public List<AgentDeviceGroupDto> DeviceGroups { get; set; } = [];
}

/// <summary>
/// Agent 角色保存请求模型
/// </summary>
public sealed class AgentRoleSaveRequest
{
    /// <summary>
    /// 角色名称
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 权限集合
    /// </summary>
    [JsonProperty("permissions")]
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// 角色绑定的设备分组 ID 集合
    /// </summary>
    [JsonProperty("deviceGroupIds")]
    public List<int> DeviceGroupIds { get; set; } = [];
}

/// <summary>
/// Agent 角色保存响应模型
/// </summary>
internal sealed class AgentRoleSaveResponse
{
    /// <summary>
    /// 保存是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 保存后的角色
    /// </summary>
    [JsonProperty("role")]
    public AgentRoleDto Role { get; set; } = new();
}

/// <summary>
/// Agent 权限描述模型
/// </summary>
public sealed class AgentPermissionDto
{
    /// <summary>
    /// 权限代码
    /// </summary>
    [JsonProperty(nameof(Code))]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 权限描述
    /// </summary>
    [JsonProperty(nameof(Description))]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Agent 设备信息模型
/// </summary>
public sealed class AgentDeviceDto
{
    /// <summary>
    /// 服务端设备 ID
    /// </summary>
    [JsonProperty(nameof(Id))]
    public int Id { get; set; }

    /// <summary>
    /// 设备名称
    /// </summary>
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 设备序列号
    /// </summary>
    [JsonProperty(nameof(Serial))]
    public string Serial { get; set; } = string.Empty;

    /// <summary>
    /// 设备 IP 地址
    /// </summary>
    [JsonProperty(nameof(IpAddress))]
    public string? IpAddress { get; set; }

    /// <summary>
    /// 设备连接端口
    /// </summary>
    [JsonProperty(nameof(Port))]
    public int? Port { get; set; }

    /// <summary>
    /// 设备状态文本
    /// </summary>
    [JsonProperty(nameof(Status))]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 当前设备所属的 Agent 设备分组
    /// </summary>
    [JsonProperty(nameof(Groups))]
    public List<AgentDeviceGroupDto> Groups { get; set; } = [];
}

/// <summary>
/// Agent 设备分组模型
/// </summary>
public sealed class AgentDeviceGroupDto
{
    /// <summary>
    /// 服务端分组 ID
    /// </summary>
    [JsonProperty(nameof(Id))]
    public int Id { get; set; }

    /// <summary>
    /// 分组名称
    /// </summary>
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 分组描述
    /// </summary>
    [JsonProperty(nameof(Description))]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 当前分组下的设备数量
    /// </summary>
    [JsonProperty(nameof(DeviceCount))]
    public int DeviceCount { get; set; }

    /// <summary>
    /// 当前分组关联的角色数量
    /// </summary>
    [JsonProperty(nameof(RoleCount))]
    public int RoleCount { get; set; }

    /// <summary>
    /// 当前分组关联的用户数量
    /// </summary>
    [JsonProperty(nameof(UserCount))]
    public int UserCount { get; set; }

    /// <summary>
    /// 是否为 Agent 内置分组
    /// </summary>
    [JsonProperty(nameof(IsInternal))]
    public bool IsInternal { get; set; }
}

/// <summary>
/// Agent 设备分组列表响应模型
/// </summary>
internal sealed class AgentDeviceGroupListResponse
{
    /// <summary>
    /// 分组列表
    /// </summary>
    [JsonProperty("items")]
    public List<AgentDeviceGroupDto> Items { get; set; } = [];
}

/// <summary>
/// Agent 设备分组保存响应模型
/// </summary>
internal sealed class AgentDeviceGroupSaveResponse
{
    /// <summary>
    /// 保存是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 保存后的分组
    /// </summary>
    [JsonProperty("group")]
    public AgentDeviceGroupDto Group { get; set; } = new();
}

/// <summary>
/// Agent 设备所属分组响应模型
/// </summary>
internal sealed class AgentDeviceGroupsResponse
{
    /// <summary>
    /// 保存是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 设备所属分组列表
    /// </summary>
    [JsonProperty("groups")]
    public List<AgentDeviceGroupDto> Groups { get; set; } = [];
}

/// <summary>
/// Agent 新增设备请求模型
/// </summary>
public sealed class AgentCreateDeviceRequest
{
    /// <summary>
    /// 设备序列号
    /// </summary>
    [JsonProperty(nameof(Serial))]
    public string Serial { get; set; } = string.Empty;

    /// <summary>
    /// 自定义设备名称
    /// </summary>
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 无线配对端口
    /// </summary>
    [JsonProperty(nameof(PairingPort))]
    public int PairingPort { get; set; }

    /// <summary>
    /// 无线配对码
    /// </summary>
    [JsonProperty(nameof(PairingCode))]
    public string PairingCode { get; set; } = string.Empty;
}

/// <summary>
/// Agent WebRTC 票据创建请求模型
/// </summary>
public sealed class AgentWebRtcTicketRequest
{
    /// <summary>
    /// 服务端设备标识
    /// </summary>
    [JsonProperty("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 目标应用包名
    /// </summary>
    [JsonProperty("appPackage")]
    public string AppPackage { get; set; } = string.Empty;

    /// <summary>
    /// 目标应用显示名称
    /// </summary>
    [JsonProperty("appName")]
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// 是否请求创建新显示
    /// </summary>
    [JsonProperty("newDisplay")]
    public bool NewDisplay { get; set; }

    /// <summary>
    /// 新显示宽度
    /// </summary>
    [JsonProperty("newDisplayWidth")]
    public int? NewDisplayWidth { get; set; }

    /// <summary>
    /// 新显示高度
    /// </summary>
    [JsonProperty("newDisplayHeight")]
    public int? NewDisplayHeight { get; set; }

    /// <summary>
    /// 新显示 DPI
    /// </summary>
    [JsonProperty("newDisplayDpi")]
    public int? NewDisplayDpi { get; set; }
}

/// <summary>
/// Agent WebRTC 票据响应模型
/// </summary>
public sealed class AgentWebRtcTicketResponse
{
    /// <summary>
    /// 一次性会话票据
    /// </summary>
    [JsonProperty("ticket")]
    public string Ticket { get; set; } = string.Empty;

    /// <summary>
    /// 当前 WebRTC 会话标识
    /// </summary>
    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 票据剩余有效期
    /// </summary>
    [JsonProperty("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }
}

/// <summary>
/// Agent 全局 WebRTC 网络设置模型
/// </summary>
public sealed class AgentWebRtcNetworkSettingsDto
{
    /// <summary>
    /// ICE 传输策略
    /// </summary>
    [JsonProperty(nameof(IceTransportPolicy))]
    public string IceTransportPolicy { get; set; } = "all";

    /// <summary>
    /// ICE 服务器列表
    /// </summary>
    [JsonProperty(nameof(IceServers))]
    public List<AgentWebRtcIceServerDto> IceServers { get; set; } = [];

    /// <summary>
    /// 是否启用 Host Candidate 覆写
    /// </summary>
    [JsonProperty(nameof(HostCandidateOverrideEnabled))]
    public bool HostCandidateOverrideEnabled { get; set; }

    /// <summary>
    /// Host Candidate 覆写地址列表
    /// </summary>
    [JsonProperty(nameof(HostCandidateOverrideIPs))]
    public List<string> HostCandidateOverrideIPs { get; set; } = [];

    /// <summary>
    /// Host Candidate 端口范围起始端口
    /// </summary>
    [JsonProperty(nameof(HostCandidatePortMin))]
    public int? HostCandidatePortMin { get; set; }

    /// <summary>
    /// Host Candidate 端口范围结束端口
    /// </summary>
    [JsonProperty(nameof(HostCandidatePortMax))]
    public int? HostCandidatePortMax { get; set; }

    /// <summary>
    /// 是否启用单端口复用模式
    /// </summary>
    [JsonProperty(nameof(SinglePortMuxEnabled))]
    public bool SinglePortMuxEnabled { get; set; }

    /// <summary>
    /// 单端口复用绑定端口
    /// </summary>
    [JsonProperty(nameof(SinglePortMuxBindPort))]
    public int? SinglePortMuxBindPort { get; set; }

    /// <summary>
    /// 单端口复用发布端口
    /// </summary>
    [JsonProperty(nameof(SinglePortMuxPublishPort))]
    public int? SinglePortMuxPublishPort { get; set; }
}

/// <summary>
/// Agent WebRTC ICE 服务器模型
/// </summary>
public sealed class AgentWebRtcIceServerDto
{
    /// <summary>
    /// ICE 服务器地址列表
    /// </summary>
    [JsonProperty(nameof(Urls))]
    public List<string> Urls { get; set; } = [];

    /// <summary>
    /// TURN 用户名
    /// </summary>
    [JsonProperty(nameof(Username))]
    public string? Username { get; set; }

    /// <summary>
    /// TURN 密码
    /// </summary>
    [JsonProperty(nameof(Credential))]
    public string? Credential { get; set; }
}

/// <summary>
/// Agent 设备设置模型
/// </summary>
public sealed class AgentDeviceSettingsDto
{
    [JsonProperty(nameof(Video))]
    public bool Video { get; set; }

    [JsonProperty(nameof(Audio))]
    public bool Audio { get; set; }

    [JsonProperty(nameof(Control))]
    public bool Control { get; set; }

    [JsonProperty(nameof(VideoCodec))]
    public string VideoCodec { get; set; } = string.Empty;

    [JsonProperty(nameof(MaxSize))]
    public int? MaxSize { get; set; }

    [JsonProperty(nameof(VideoBitRate))]
    public int? VideoBitRate { get; set; }

    [JsonProperty(nameof(MaxFps))]
    public double? MaxFps { get; set; }

    [JsonProperty(nameof(AudioCodec))]
    public string AudioCodec { get; set; } = string.Empty;

    [JsonProperty(nameof(AudioBitRate))]
    public int? AudioBitRate { get; set; }

    [JsonProperty(nameof(VideoSource))]
    public string VideoSource { get; set; } = string.Empty;

    [JsonProperty(nameof(AudioSource))]
    public string AudioSource { get; set; } = string.Empty;

    [JsonProperty(nameof(StayAwake))]
    public bool StayAwake { get; set; }

    [JsonProperty(nameof(ShowTouches))]
    public bool ShowTouches { get; set; }

    [JsonProperty(nameof(PowerOn))]
    public bool PowerOn { get; set; }

    [JsonProperty(nameof(PowerOffOnClose))]
    public bool PowerOffOnClose { get; set; }

    [JsonProperty(nameof(ScreenOffTimeout))]
    public int? ScreenOffTimeout { get; set; }

    [JsonProperty(nameof(HidKeyboard))]
    public bool HidKeyboard { get; set; }

    [JsonProperty(nameof(HidMouse))]
    public bool HidMouse { get; set; }

    [JsonProperty(nameof(CameraFacing))]
    public string CameraFacing { get; set; } = string.Empty;

    [JsonProperty(nameof(CameraId))]
    public string CameraId { get; set; } = string.Empty;

    [JsonProperty(nameof(CameraSize))]
    public string CameraSize { get; set; } = string.Empty;

    [JsonProperty(nameof(CameraFps))]
    public string CameraFps { get; set; } = string.Empty;

    [JsonProperty(nameof(CameraHighSpeed))]
    public bool CameraHighSpeed { get; set; }

    [JsonProperty(nameof(AudioDup))]
    public bool AudioDup { get; set; }

    [JsonProperty(nameof(VdDestroyContent))]
    public bool VdDestroyContent { get; set; }

    [JsonProperty(nameof(VdSystemDecorations))]
    public bool VdSystemDecorations { get; set; }

    [JsonProperty(nameof(NewDisplay))]
    public string NewDisplay { get; set; } = string.Empty;

    [JsonProperty(nameof(FlexDisplay))]
    public bool FlexDisplay { get; set; }

    [JsonProperty(nameof(VideoEncoder))]
    public string VideoEncoder { get; set; } = string.Empty;

    [JsonProperty(nameof(AudioEncoder))]
    public string AudioEncoder { get; set; } = string.Empty;

    [JsonProperty(nameof(CodecOptions))]
    public string CodecOptions { get; set; } = string.Empty;
}

/// <summary>
/// Agent 服务端语言设置响应模型
/// </summary>
public sealed class AgentLanguageSettingResponse
{
    /// <summary>
    /// 当前服务端语言区域代码
    /// </summary>
    [JsonProperty("locale")]
    public string Locale { get; set; } = string.Empty;
}

/// <summary>
/// Agent 可用语言选项模型
/// </summary>
public sealed class AgentLanguageOptionDto
{
    /// <summary>
    /// 语言区域代码
    /// </summary>
    [JsonProperty("locale")]
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// 语言显示名称
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Agent 设备剪贴板响应模型
/// </summary>
public sealed class AgentClipboardResponse
{
    /// <summary>
    /// 操作是否成功
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 剪贴板文本
    /// </summary>
    [JsonProperty("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 当前文本是否来自服务端缓存
    /// </summary>
    [JsonProperty("cached")]
    public bool Cached { get; set; }

    /// <summary>
    /// 是否执行了粘贴动作
    /// </summary>
    [JsonProperty("paste")]
    public bool Paste { get; set; }
}

/// <summary>
/// Agent 应用摘要模型
/// </summary>
public sealed class AgentAppDto
{
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    [JsonProperty(nameof(PackageName))]
    public string PackageName { get; set; } = string.Empty;
}

/// <summary>
/// Agent 应用详情模型
/// </summary>
public sealed class AgentAppInfoDto
{
    [JsonProperty("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [JsonProperty("versionName")]
    public string VersionName { get; set; } = string.Empty;

    [JsonProperty("versionCode")]
    public string VersionCode { get; set; } = string.Empty;

    [JsonProperty("firstInstallTime")]
    public string FirstInstallTime { get; set; } = string.Empty;

    [JsonProperty("lastUpdateTime")]
    public string LastUpdateTime { get; set; } = string.Empty;

    [JsonProperty("installerPackageName")]
    public string InstallerPackageName { get; set; } = string.Empty;

    [JsonProperty("primaryApkPath")]
    public string PrimaryApkPath { get; set; } = string.Empty;

    [JsonProperty("apkPaths")]
    public List<string> ApkPaths { get; set; } = [];
}

/// <summary>
/// Agent 文件列表响应模型
/// </summary>
public sealed class AgentFileListResponse
{
    [JsonProperty("path")]
    public string Path { get; set; } = string.Empty;

    [JsonProperty("items")]
    public List<AgentFileEntryDto> Items { get; set; } = [];
}

/// <summary>
/// Agent 文件条目模型
/// </summary>
public sealed class AgentFileEntryDto
{
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    [JsonProperty(nameof(IsDirectory))]
    public bool IsDirectory { get; set; }

    [JsonProperty(nameof(Size))]
    public ulong Size { get; set; }
}

/// <summary>
/// Agent 下载响应包装
/// </summary>
public sealed class AgentDownloadResponse(HttpResponseMessage response) : IDisposable
{
    private readonly HttpResponseMessage _response = response;

    /// <summary>
    /// 下载内容流
    /// </summary>
    public Stream Stream => _response.Content.ReadAsStream();

    /// <summary>
    /// 响应中给出的文件名
    /// </summary>
    public string FileName => ParseFileName(_response.Content.Headers.ContentDisposition?.FileName) ?? "download.bin";

    public void Dispose()
    {
        _response.Dispose();
    }

    private static string? ParseFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Trim('"');
    }
}

/// <summary>
/// Agent 错误响应模型
/// </summary>
internal sealed class AgentErrorResponse
{
    /// <summary>
    /// 服务端返回的错误对象
    /// </summary>
    [JsonProperty("error")]
    public AgentErrorPayload? Error { get; set; }
}

/// <summary>
/// Agent 错误负载模型
/// </summary>
internal sealed class AgentErrorPayload
{
    /// <summary>
    /// 服务端错误代码
    /// </summary>
    [JsonProperty("code")]
    public string? Code { get; set; }

    /// <summary>
    /// 服务端返回的本地化键
    /// </summary>
    [JsonProperty("messageKey")]
    public string? MessageKey { get; set; }

    /// <summary>
    /// 服务端返回的错误消息
    /// </summary>
    [JsonProperty("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Agent scrcpy 会话动作请求模型
/// </summary>
internal sealed class AgentScrcpySessionActionRequest
{
    /// <summary>
    /// 服务端设备标识
    /// </summary>
    [JsonProperty("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 当前会话标识
    /// </summary>
    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}
