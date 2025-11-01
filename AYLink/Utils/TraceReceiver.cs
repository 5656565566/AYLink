using AdvancedSharpAdbClient.Receivers;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.Utils;

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

        if (_outputAction != null)
        {
            _outputAction(line);
        }

        return Task.FromResult(true);
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool AddOutput(string line)
    {
        Debug.WriteLine(line);

        if (_outputAction != null)
        {
            _outputAction(line);
        }

        return true;
    }

    public void Flush()
    {
    }
}