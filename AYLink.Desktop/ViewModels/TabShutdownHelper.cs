using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// 提供标签页停止与销毁的统一辅助方法
/// </summary>
internal static class TabShutdownHelper
{
    /// <summary>
    /// 先异步停止标签页，再释放托管资源
    /// </summary>
    /// <param name="tab">待关闭的标签页</param>
    public static async Task StopAndDisposeAsync(TabItemViewModelBase tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (tab is IAsyncTabStopAware asyncStopAware)
        {
            _ = RunStopInBackgroundAsync(asyncStopAware);
        }

        DisposeTab(tab);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 在同步销毁路径上尽量完成停止和资源释放
    /// </summary>
    /// <param name="tab">待关闭的标签页</param>
    public static void StopAndDispose(TabItemViewModelBase tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (tab is IAsyncTabStopAware asyncStopAware)
        {
            _ = RunStopInBackgroundAsync(asyncStopAware);
        }

        DisposeTab(tab);
    }

    /// <summary>
    /// 在后台尽力执行标签页停止逻辑，避免阻塞前台关闭流程
    /// </summary>
    /// <param name="asyncStopAware">支持异步停止的标签页</param>
    private static async Task RunStopInBackgroundAsync(IAsyncTabStopAware asyncStopAware)
    {
        try
        {
            await asyncStopAware.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TabShutdown] StopAsync failed: {ex}");
        }
    }

    /// <summary>
    /// 执行标签页的本地资源释放
    /// </summary>
    /// <param name="tab">待释放的标签页</param>
    private static void DisposeTab(TabItemViewModelBase tab)
    {
        if (tab is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
