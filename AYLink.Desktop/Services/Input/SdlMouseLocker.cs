using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using SDL;
using static SDL.SDL3;

namespace AYLink.Desktop.Services.Input;

internal unsafe class SdlMouseLocker : Control
{
    private static SdlMouseLocker? _activeLocker;
    private SDL_Window* _sdlWindow = null;
    private bool _isMouseLockApplied;

    /// <summary>
    /// 鼠标是否锁定
    /// </summary>
    public bool IsMouseLocked
    {
        get => GetValue(IsMouseLockedProperty);
        set => SetValue(IsMouseLockedProperty, value);
    }

    /// <summary>
    /// 窗口句柄是否关联到程序窗口
    /// </summary>
    public bool HasSdlWindow => _sdlWindow != null;

    public static readonly StyledProperty<bool> IsMouseLockedProperty =
        AvaloniaProperty.Register<SdlMouseLocker, bool>(nameof(IsMouseLocked));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _activeLocker = this;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            if (topLevel is Window window)
            {
                window.Activated += Window_Activated;
                window.Deactivated += Window_Deactivated;
            }
        }

        if (IsMouseLocked)
        {
            ScheduleMouseLock(true);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ReferenceEquals(_activeLocker, this))
        {
            _activeLocker = null;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window)
        {
            window.Activated -= Window_Activated;
            window.Deactivated -= Window_Deactivated;
        }

        ReleaseSdlWindow();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (IsMouseLocked)
        {
            ScheduleMouseLock(true);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IsMouseLocked)
        {
            // 窗口失去焦点时 暂时解除 SDL 鼠标锁定和隐藏 但不改变 IsMouseLocked 属性状态
            ScheduleMouseLock(false);
        }
    }

    private void AttachToSdl()
    {
        if (_sdlWindow != null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        var platformHandle = topLevel?.TryGetPlatformHandle();
        if (platformHandle == null) return;

        HideSystemCursor();

        var props = SDL_CreateProperties();

        if (!SetWindowProperties(props, platformHandle))
        {
            SDL_DestroyProperties(props);
            return;
        }

        _sdlWindow = SDL_CreateWindowWithProperties(props);
        SDL_DestroyProperties(props);

        if (_sdlWindow == null)
        {
            Debug.WriteLine($"[SdlMouseLocker] SDL_CreateWindowWithProperties fail: {SDL_GetError()}");
        }
        else
        {
            Debug.WriteLine($"[SdlMouseLocker] {platformHandle.HandleDescriptor} bound to SDL window.");
        }
    }

    private static bool SetWindowProperties(SDL_PropertiesID props, IPlatformHandle handle)
    {
        switch (handle.HandleDescriptor)
        {
            case "HWND":
                SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_WIN32_HWND_POINTER, handle.Handle);
                return true;
            case "NSWindow":
                SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_COCOA_WINDOW_POINTER, handle.Handle);
                return true;
            case "NSView":
                SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_COCOA_VIEW_POINTER, handle.Handle);
                return true;
            case "XID":
                SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_X11_WINDOW_NUMBER, handle.Handle);
                return true;
            case "WaylandSurface":
            case "wl_surface":
            case "Wayland":
                SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_WAYLAND_WL_SURFACE_POINTER, handle.Handle);
                return true;
            default:
                Debug.WriteLine($"[SdlMouseLocker] Unknown host handle: {handle.HandleDescriptor}");
                return false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsMouseLockedProperty)
        {
            ScheduleMouseLock(change.GetNewValue<bool>());
        }
    }

    private void ScheduleMouseLock(bool enable)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => SetMouseLock(enable),
            Avalonia.Threading.DispatcherPriority.Loaded
        );
    }

    private void HideSystemCursor()
    {
        Cursor = new Cursor(StandardCursorType.None);
    }

    public static Vector GetRelativeMouseDelta()
    {
        if (_activeLocker == null || _activeLocker._sdlWindow == null || !_activeLocker.IsMouseLocked)
        {
            return default;
        }

        float dx = 0;
        float dy = 0;
        SDL_GetRelativeMouseState(&dx, &dy);
        return new Vector(dx, dy);
    }
    private void SetMouseLock(bool enable)
    {
        var topLevel = TopLevel.GetTopLevel(this);

        if (_isMouseLockApplied == enable)
        {
            return;
        }

        if (enable)
        {
            AttachToSdl();
            if (_sdlWindow == null) return;

            if (topLevel != null)
            {
                topLevel.Cursor = new Cursor(StandardCursorType.None);
            }
            else
            {
                Cursor = new Cursor(StandardCursorType.None);
            }

            SDL_SetWindowMouseGrab(_sdlWindow, true);
            SDL_CaptureMouse(true);
            SDL_HideCursor();
            _isMouseLockApplied = true;
        }
        else
        {
            ReleaseSdlWindow();

            if (topLevel != null)
            {
                topLevel.Cursor = Cursor.Default;
            }
            else
            {
                Cursor = Cursor.Default;
            }

            _isMouseLockApplied = false;
        }
    }
    private void ReleaseSdlWindow()
    {
        if (_sdlWindow == null)
        {
            _isMouseLockApplied = false;
            return;
        }

        // 恢复正常
        SDL_CaptureMouse(false);
        SDL_SetWindowMouseGrab(_sdlWindow, false);
        SDL_ShowCursor();

        SDL_DestroyWindow(_sdlWindow);
        _sdlWindow = null;
        _isMouseLockApplied = false;

        Debug.WriteLine("[SdlMouseLocker] SDL window destroyed and detached.");
    }
}
