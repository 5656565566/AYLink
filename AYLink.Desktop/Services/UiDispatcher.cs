using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AYLink.Desktop.Services;

public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    Task InvokeAsync(Action action);

    Task<T> InvokeAsync<T>(Func<T> func);

    Task InvokeAsync(Func<Task> action);

    Task<T> InvokeAsync<T>(Func<Task<T>> func);
}

public sealed class UiDispatcher : IUiDispatcher
{
    public static IUiDispatcher Instance { get; } = new UiDispatcher();

    private UiDispatcher()
    {
    }

    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.Post(action);
    }

    public async Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return await Dispatcher.UIThread.InvokeAsync(func);
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return await Dispatcher.UIThread.InvokeAsync(func);
    }
}
