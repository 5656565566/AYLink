using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using SDL;
using static SDL.SDL3;

namespace AYLink.Desktop.Services.Input;

internal unsafe class SdlMouseLockService : IMouseLockService
{
    private SDL_Window* _sdlWindow;
    private Control? _target;
    private TopLevel? _topLevel;
    private bool _isMouseLockApplied;
    private bool _isLocked;

    public bool IsLocked => _isLocked;

    public void Attach(Control target)
    {
        if (ReferenceEquals(_target, target))
        {
            return;
        }

        Detach();

        _target = target;
        _topLevel = TopLevel.GetTopLevel(target);

        if (_topLevel is Window window)
        {
            window.Activated += Window_Activated;
            window.Deactivated += Window_Deactivated;
        }

        if (_isLocked)
        {
            ScheduleMouseLock(true);
        }
    }

    public void Detach()
    {
        if (_topLevel is Window window)
        {
            window.Activated -= Window_Activated;
            window.Deactivated -= Window_Deactivated;
        }

        ReleaseMouseLock();
        _topLevel = null;
        _target = null;
    }

    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        ScheduleMouseLock(locked);
    }

    public void WarpCursorInTarget(Point point)
    {
        if (_sdlWindow == null || _target == null || _topLevel == null)
        {
            return;
        }

        var pointInWindow = _target.TranslatePoint(point, _topLevel);
        if (!pointInWindow.HasValue)
        {
            return;
        }

        SDL_WarpMouseInWindow(_sdlWindow, (float)pointInWindow.Value.X, (float)pointInWindow.Value.Y);
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (_isLocked)
        {
            ScheduleMouseLock(true);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isLocked)
        {
            ScheduleMouseLock(false);
        }
    }

    private void ScheduleMouseLock(bool enable)
    {
        Dispatcher.UIThread.Post(
            () => ApplyMouseLock(enable),
            DispatcherPriority.Loaded);
    }

    private void ApplyMouseLock(bool enable)
    {
        if (_isMouseLockApplied == enable)
        {
            return;
        }

        if (enable)
        {
            AttachToSdl();
            if (_sdlWindow == null)
            {
                return;
            }

            SetCursorVisible(false);
            SDL_SetWindowMouseGrab(_sdlWindow, true);
            SDL_CaptureMouse(true);
            SDL_HideCursor();
            _isMouseLockApplied = true;
            return;
        }

        ReleaseMouseLock();
    }

    private void AttachToSdl()
    {
        if (_sdlWindow != null)
        {
            return;
        }

        var platformHandle = _topLevel?.TryGetPlatformHandle();
        if (platformHandle == null)
        {
            return;
        }

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
            Debug.WriteLine($"[SdlMouseLockService] SDL_CreateWindowWithProperties fail: {SDL_GetError()}");
        }
        else
        {
            Debug.WriteLine($"[SdlMouseLockService] {platformHandle.HandleDescriptor} bound to SDL window.");
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
                Debug.WriteLine($"[SdlMouseLockService] Unknown host handle: {handle.HandleDescriptor}");
                return false;
        }
    }

    private void ReleaseMouseLock()
    {
        if (_sdlWindow != null)
        {
            SDL_CaptureMouse(false);
            SDL_SetWindowMouseGrab(_sdlWindow, false);
            SDL_ShowCursor();
            SDL_DestroyWindow(_sdlWindow);
            _sdlWindow = null;

            Debug.WriteLine("[SdlMouseLockService] SDL window destroyed and detached.");
        }

        SetCursorVisible(true);
        _isMouseLockApplied = false;
    }

    private void SetCursorVisible(bool visible)
    {
        var cursor = visible ? Cursor.Default : new Cursor(StandardCursorType.None);

        if (_topLevel != null)
        {
            _topLevel.Cursor = cursor;
        }

        if (_target != null)
        {
            _target.Cursor = cursor;
        }
    }

    public void Dispose()
    {
        _isLocked = false;
        Detach();
    }
}
