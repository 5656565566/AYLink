using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Agent;
using AYLink.Core.Models;

namespace AYLink.Desktop.Services.Agent;

/// <summary>
/// 远程 Agent 应用管理封装
/// </summary>
public sealed class AgentAppService(AgentServerRuntime runtime, int remoteDeviceId)
{
    private readonly AgentServerRuntime _runtime = runtime;
    private readonly int _remoteDeviceId = remoteDeviceId;

    /// <summary>
    /// 获取远程设备应用列表
    /// </summary>
    public async Task<IReadOnlyList<AppInfo>> ListAppsAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var apps = await _runtime.Client.GetAppsAsync(accessToken, _remoteDeviceId, cancellationToken);
        _runtime.TouchSuccess();
        return apps.Select(item => new AppInfo(item.Name, item.PackageName)).ToList();
    }

    /// <summary>
    /// 启动远程应用
    /// </summary>
    public async Task LaunchAsync(string packageName, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        await _runtime.Client.LaunchAppAsync(accessToken, _remoteDeviceId, packageName, cancellationToken);
        _runtime.TouchSuccess();
    }

    /// <summary>
    /// 卸载远程应用
    /// </summary>
    public async Task UninstallAsync(string packageName, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        await _runtime.Client.UninstallAppAsync(accessToken, _remoteDeviceId, packageName, cancellationToken);
        _runtime.TouchSuccess();
    }

    /// <summary>
    /// 获取远程应用详情
    /// </summary>
    public async Task<AgentAppInfoDto> GetInfoAsync(string packageName, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var result = await _runtime.Client.GetAppInfoAsync(accessToken, _remoteDeviceId, packageName, cancellationToken);
        _runtime.TouchSuccess();
        return result;
    }

    /// <summary>
    /// 下载远程应用 APK
    /// </summary>
    public async Task DownloadAsync(string packageName, string localFilePath, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        using var response = await _runtime.Client.DownloadAppAsync(accessToken, _remoteDeviceId, packageName, cancellationToken);
        await using var output = File.Create(localFilePath);
        await response.Stream.CopyToAsync(output, cancellationToken);
        _runtime.TouchSuccess();
    }

    /// <summary>
    /// 上传本地 APK 到远程设备并安装
    /// </summary>
    public async Task InstallAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        await using var stream = File.OpenRead(filePath);
        await _runtime.Client.InstallAppAsync(accessToken, _remoteDeviceId, Path.GetFileName(filePath), stream, cancellationToken);
        _runtime.TouchSuccess();
    }
}
