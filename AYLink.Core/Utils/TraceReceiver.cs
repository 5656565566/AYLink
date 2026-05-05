using AdvancedSharpAdbClient.Receivers;
using System.Diagnostics;

namespace AYLink.Core.Utils;

/// <summary>
/// 虚拟命令接收区
/// </summary>
public class TraceReceiver : IShellOutputReceiver
{
    private Action<string>? _outputAction;

    public static bool ParsesErrors => false;

    public void SetOutput(Action<string> outputAction)
    {
        _outputAction = outputAction;
    }

    public Task<bool> AddOutputAsync(string line, CancellationToken cancellationToken)
    {
        Debug.WriteLine(line);
        _outputAction?.Invoke(line);

        return Task.FromResult(true);
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool AddOutput(string line)
    {
        Debug.WriteLine(line);
        _outputAction?.Invoke(line);

        return true;
    }

    public void Flush()
    {
    }
}
