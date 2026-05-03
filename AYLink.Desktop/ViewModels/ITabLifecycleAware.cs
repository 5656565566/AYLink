using System;

namespace AYLink.Desktop.ViewModels;

public interface ITabLifecycleAware : IDisposable
{
    void OnAttachedToHost();

    void OnDetachedFromHost();
}
