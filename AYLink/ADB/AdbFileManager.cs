using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AYLink.UIModel;
using System.Diagnostics;

namespace AYLink.ADB;

/// <summary>
/// 使用 AdvancedSharpAdbClient 管理安卓设备上的文件。
/// 此类中的所有 I/O 操作都设计为异步，以避免阻塞调用线程。
/// </summary>
/// <remarks>
/// 初始化一个新的 AdbFileManager 实例。
/// </remarks>
/// <param name="client">一个已初始化的 AdbClient 实例。</param>
/// <param name="device">要进行文件操作的目标设备。</param>
public class AdbFileManager(AdbClient client, DeviceData device)
{
    private readonly AdbClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly DeviceData _device = device;

    /// <summary>
    /// 异步列出指定远程路径下的文件和目录。
    /// </summary>
    /// <param name="remotePath">设备上的绝对路径 (例如 "/sdcard/")。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>一个包含 FileSystemModel 的可观察集合，代表目录内容。</returns>
    internal async Task<ObservableCollection<FileSystemModel>> ListDirectoryAsync(string remotePath = "/sdcard/", CancellationToken cancellationToken = default)
    {
        var items = new ObservableCollection<FileSystemModel>();
        try
        {
            IAsyncEnumerable<FileStatistics> fileEntries = _client.GetDirectoryAsyncListing(_device, remotePath, cancellationToken);

            items.Add(new FileSystemModel
                (
                    name: "..",
                    isDirectory: true,
                    size: 0
                )
            );

            await foreach (var entry in fileEntries.WithCancellation(cancellationToken))
            {
                string fullPath = remotePath.EndsWith("/") ? $"{remotePath}{entry.Path}" : $"{remotePath}/{entry.Path}";

                if (entry.Path == "." || entry.Path == "..")
                {
                    continue;
                }

                var item = new FileSystemModel
                (
                    name: entry.Path,
                    size: entry.Size,
                    isDirectory: entry.FileMode.HasFlag(UnixFileStatus.Directory)
                );
                items.Add(item);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"列出目录 '{remotePath}' 时出错: {ex.Message}");
        }
        return items;
    }

    /// <summary>
    /// 异步将文件从设备下载到本地计算机。
    /// </summary>
    /// <param name="remoteFilePath">设备上要下载的文件的完整路径。</param>
    /// <param name="localFilePath">要保存文件的本地完整路径。</param>
    /// <param name="progress">用于报告下载进度的回调。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public async Task DownloadFileAsync(string remoteFilePath, string localFilePath, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        // 确保本地目标目录存在
        string? directoryName = Path.GetDirectoryName(localFilePath);
        if (!string.IsNullOrEmpty(directoryName))
        {
            Directory.CreateDirectory(directoryName);
        }

        // SyncService 和 FileStream 都需要被正确释放
        using var service = new SyncService(new AdbSocket(_client.EndPoint), _device);
        await using var stream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write);

        void progressAction(SyncProgressChangedEventArgs e) => progress?.Report(e.ProgressPercentage);

        await service.PullAsync(remoteFilePath, stream, progressAction, cancellationToken);
        progress?.Report(100.0);
    }

    /// <summary>
    /// 异步将本地文件上传到设备。
    /// </summary>
    /// <param name="localFilePath">要上传的本地文件的完整路径。</param>
    /// <param name="remoteFilePath">设备上保存文件的完整路径。</param>
    /// <param name="progress">用于报告上传进度的回调。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public async Task UploadFileAsync(string localFilePath, string remoteFilePath, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        using var service = new SyncService(new AdbSocket(_client.EndPoint), _device);
        await using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);

        // 创建一个委托来报告进度
        void progressAction(SyncProgressChangedEventArgs e) => progress?.Report(e.ProgressPercentage);

        UnixFileStatus permissions = UnixFileStatus.DefaultFileMode;

        await service.PushAsync(stream, remoteFilePath, permissions, DateTimeOffset.Now, progressAction, cancellationToken);
        progress?.Report(100.0); // 确保最后报告100% 保证进度弹窗可以主动关闭
    }

    /// <summary>
    /// 在设备上异步移动或重命名文件/目录。
    /// </summary>
    /// <param name="oldPath">原始的完整路径。</param>
    /// <param name="newPath">新的完整路径。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>操作成功时返回 true，否则返回 false。</returns>
    public async Task<bool> MoveFileAsync(string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        try
        {
            string command = $"mv \"{oldPath}\" \"{newPath}\"";
            var receiver = new ConsoleOutputReceiver();

            await _client.ExecuteShellCommandAsync(_device, command, receiver, cancellationToken);

            string output = receiver.ToString();

            if (!string.IsNullOrWhiteSpace(output) &&
                (output.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)))
            {
                Debug.WriteLine($"移动文件 '{oldPath}' 到 '{newPath}' 时出错: {output}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"执行移动命令时发生异常: {ex.Message}");
            return false;
        }
    }
}