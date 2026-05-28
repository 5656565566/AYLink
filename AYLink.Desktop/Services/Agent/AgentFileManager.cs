using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AYLink.Core.Agent;
using AYLink.Core.Models;

namespace AYLink.Desktop.Services.Agent;

/// <summary>
/// 远程 Agent 文件管理封装
/// 负责将 Agent 文件接口适配为桌面端文件页可消费的方法
/// </summary>
public sealed class AgentFileManager(AgentServerRuntime runtime, int remoteDeviceId)
{
    private readonly AgentServerRuntime _runtime = runtime;
    private readonly int _remoteDeviceId = remoteDeviceId;

    /// <summary>
    /// 列出指定目录下的文件和目录
    /// </summary>
    public async Task<ObservableCollection<FileSystemModel>> ListDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var response = await _runtime.Client.ListFilesAsync(accessToken, _remoteDeviceId, remotePath, cancellationToken);
        _runtime.TouchSuccess();

        var result = new ObservableCollection<FileSystemModel>();
        if (!string.Equals(response.Path, "/sdcard/", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new FileSystemModel("..", 0, true));
        }

        foreach (var item in response.Items)
        {
            result.Add(new FileSystemModel(item.Name, (uint)Math.Min(item.Size, uint.MaxValue), item.IsDirectory));
        }

        return result;
    }

    /// <summary>
    /// 下载远程文件到本地路径
    /// </summary>
    public async Task DownloadFileAsync(string remoteFilePath, string localFilePath, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        using var download = await _runtime.Client.DownloadFileAsync(accessToken, _remoteDeviceId, remoteFilePath, cancellationToken);
        await using var fileStream = File.Create(localFilePath);
        await CopyWithProgressAsync(download.Stream, fileStream, progress, cancellationToken);
        _runtime.TouchSuccess();
    }

    /// <summary>
    /// 上传本地文件到远程设备
    /// 当前 Agent 后端尚未提供文件上传接口
    /// </summary>
    public Task UploadFileAsync(string localFilePath, string remoteFilePath, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("当前 Agent 后端尚未提供文件上传接口。");
    }

    /// <summary>
    /// 删除远程文件或目录
    /// </summary>
    public async Task<bool> DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var response = await _runtime.Client.DeleteFileAsync(accessToken, _remoteDeviceId, path, cancellationToken);
        _runtime.TouchSuccess();
        return response.Success;
    }

    /// <summary>
    /// 重命名远程文件或目录
    /// </summary>
    public async Task<bool> RenameFileAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        var accessToken = await _runtime.EnsureAccessTokenAsync(cancellationToken);
        var response = await _runtime.Client.RenameFileAsync(accessToken, _remoteDeviceId, path, newName, cancellationToken);
        _runtime.TouchSuccess();
        return response.Success;
    }

    private static async Task CopyWithProgressAsync(Stream source, Stream destination, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalRead = 0;
        var contentLength = source.CanSeek ? source.Length : -1;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            if (contentLength > 0)
            {
                progress.Report(totalRead * 100d / contentLength);
            }
        }

        progress.Report(100);
    }
}
