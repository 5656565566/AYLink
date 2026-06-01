using System;
using System.Collections.Generic;

namespace AYLink.Desktop.Models;

/// <summary>
/// 远程 Agent 服务器配置模型
/// 用于保存桌面端已添加的服务器基础信息与登录态
/// </summary>
public class AgentServerConfig
{
    /// <summary>
    /// 本地保存的服务器唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 服务器显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Agent 服务端基础地址
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 最近一次使用的登录用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 当前访问令牌
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 当前访问令牌过期时间
    /// </summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// 当前刷新令牌
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// 当前刷新令牌过期时间
    /// </summary>
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    /// <summary>
    /// 最近一次成功获取到的服务端用户名
    /// </summary>
    public string LastKnownUserName { get; set; } = string.Empty;

    /// <summary>
    /// 最近一次成功同步的时间
    /// </summary>
    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>
    /// 最近一次同步的服务端语言区域代码
    /// </summary>
    public string AgentLocale { get; set; } = string.Empty;

    /// <summary>
    /// 缓存的服务端扁平化语言字典
    /// </summary>
    public Dictionary<string, string> AgentTranslations { get; set; } = [];

    public bool EnableWebRtcOverride { get; set; } = false;
    public string LocalIceTransportPolicy { get; set; } = "all";
    public List<AgentServerIceServerConfig> LocalIceServers { get; set; } = [];
    public string IceTransportPolicy { get; set; } = "all";
    public bool EnableHostCandidateOverride { get; set; } = false;
    public string DirectHostList { get; set; } = string.Empty;
    public bool EnablePortMapping { get; set; } = false;
    public string LocalBindPort { get; set; } = string.Empty;
    public string ExternalPublishPort { get; set; } = string.Empty;
    public List<AgentServerIceServerConfig> GlobalIceServers { get; set; } = [];
}

public class AgentServerIceServerConfig
{
    public string Kind { get; set; } = "STUN";
    public string Address { get; set; } = string.Empty;
}
