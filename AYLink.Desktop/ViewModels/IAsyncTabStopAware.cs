using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels;

/// <summary>
/// 为需要异步停止的标签页提供统一生命周期入口
/// </summary>
public interface IAsyncTabStopAware
{
    /// <summary>
    /// 在销毁前异步停止标签页持有的外部会话或后台任务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
