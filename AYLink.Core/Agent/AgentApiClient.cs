using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
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
}

/// <summary>
/// Agent 布尔值结果响应模型
/// </summary>
public sealed class AgentBooleanResponse
{
    /// <summary>
    /// 当前请求是否成功
    /// </summary>
    [JsonProperty("ok")]
    public bool Success { get; set; }
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
