using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace AYLink.Desktop.Services.Input;

internal partial class ScreenInputCoordinator : IDisposable
{
    private readonly Func<IInputProcessor> _inputProcessorAccessor;
    private readonly IMouseLockService _mouseLockService;
    private readonly Func<bool> _isMouseLockedAccessor;
    private readonly Func<bool> _canHandleKeyboardInputAccessor;
    private readonly Func<bool> _isInputAvailableAccessor;
    private readonly Func<Point, Point> _normalizeCoordinates;
    private readonly Action<bool, bool> _mouseLockChanged;

    private Image? _videoImage;
    private InputElement? _keyboardEventHost;
    private IPointer? _activePointer;
    private Point? _lastPointerViewPoint;
    private int? _lastPointerHash;
    private bool _ignoreNextLockedPointerMove;
    private bool _isPointerCaptured;

    public ScreenInputCoordinator(
        Func<IInputProcessor> inputProcessorAccessor,
        IMouseLockService mouseLockService,
        Func<bool> isMouseLockedAccessor,
        Func<bool> canHandleKeyboardInputAccessor,
        Func<bool> isInputAvailableAccessor,
        Func<Point, Point> normalizeCoordinates,
        Action<bool, bool> mouseLockChanged)
    {
        _inputProcessorAccessor = inputProcessorAccessor;
        _mouseLockService = mouseLockService;
        _isMouseLockedAccessor = isMouseLockedAccessor;
        _canHandleKeyboardInputAccessor = canHandleKeyboardInputAccessor;
        _isInputAvailableAccessor = isInputAvailableAccessor;
        _normalizeCoordinates = normalizeCoordinates;
        _mouseLockChanged = mouseLockChanged;
    }

    public void Attach(Image videoImage)
    {
        if (ReferenceEquals(_videoImage, videoImage))
        {
            return;
        }

        Detach();

        _videoImage = videoImage;
        _videoImage.PointerMoved += VideoImage_PointerMoved;
        _videoImage.PointerCaptureLost += VideoImage_PointerCaptureLost;
        _videoImage.SizeChanged += VideoImage_SizeChanged;
        _videoImage.PointerWheelChanged += VideoImage_PointerWheelChanged;

        _videoImage.AddHandler(
            InputElement.PointerPressedEvent,
            VideoImage_PointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);

        _videoImage.AddHandler(
            InputElement.PointerReleasedEvent,
            VideoImage_PointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);

        _keyboardEventHost = TopLevel.GetTopLevel(_videoImage) as InputElement;
        _keyboardEventHost?.AddHandler(
            InputElement.KeyDownEvent,
            VideoImage_KeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: true);

        _keyboardEventHost?.AddHandler(
            InputElement.KeyUpEvent,
            VideoImage_KeyUp,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: true);

        _videoImage.Focusable = true;
    }

    public void Detach()
    {
        if (_videoImage == null)
        {
            return;
        }

        _videoImage.PointerMoved -= VideoImage_PointerMoved;
        _videoImage.PointerCaptureLost -= VideoImage_PointerCaptureLost;
        _videoImage.SizeChanged -= VideoImage_SizeChanged;
        _videoImage.PointerWheelChanged -= VideoImage_PointerWheelChanged;
        _videoImage.RemoveHandler(InputElement.PointerPressedEvent, VideoImage_PointerPressed);
        _videoImage.RemoveHandler(InputElement.PointerReleasedEvent, VideoImage_PointerReleased);
        _keyboardEventHost?.RemoveHandler(InputElement.KeyDownEvent, VideoImage_KeyDown);
        _keyboardEventHost?.RemoveHandler(InputElement.KeyUpEvent, VideoImage_KeyUp);
        _keyboardEventHost = null;

        _inputProcessorAccessor().ClearAll();
        ResetPointerCapture();
        ResetPointerMotionTracking();
        _videoImage = null;
    }

    public void SetMouseLocked(bool locked)
    {
        ResetPointerMotionTracking();
        _ignoreNextLockedPointerMove = false;

        if (!locked)
        {
            _activePointer?.Capture(null);
            _isPointerCaptured = false;
            return;
        }

        CenterCursorInVideoImage();

    }

    private void VideoImage_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!_canHandleKeyboardInputAccessor())
        {
            return;
        }

        if (_isMouseLockedAccessor() && e.Key == Key.Escape)
        {
            _mouseLockChanged(false, true);
            e.Handled = true;
            return;
        }

        if (_inputProcessorAccessor() is HidInputProcessor && IsAltToggleKey(e))
        {
            _mouseLockChanged(!_isMouseLockedAccessor(), true);
            e.Handled = true;
            return;
        }

        _inputProcessorAccessor().ProcessKey(new KeyInput
        {
            EventType = KeyEventType.Down,
            KeyName = e.Key.ToString()
        });

        if (_isMouseLockedAccessor())
        {
            e.Handled = true;
        }
    }

    private void VideoImage_KeyUp(object? sender, KeyEventArgs e)
    {
        if (!_canHandleKeyboardInputAccessor())
        {
            return;
        }

        if (_inputProcessorAccessor() is HidInputProcessor && IsAltToggleKey(e))
        {
            e.Handled = true;
            return;
        }

        _inputProcessorAccessor().ProcessKey(new KeyInput
        {
            EventType = KeyEventType.Up,
            KeyName = e.Key.ToString()
        });

        if (_isMouseLockedAccessor())
        {
            e.Handled = true;
        }
    }

    private void VideoImage_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _inputProcessorAccessor().ClearAll();
        ResetPointerCapture();
        ResetPointerMotionTracking();
    }

    private void VideoImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isInputAvailableAccessor() || _videoImage == null)
        {
            return;
        }

        _activePointer = e.Pointer;

        if (!_videoImage.IsFocused)
        {
            _videoImage.Focus();
        }

        var viewPoint = e.GetPosition(_videoImage);
        TrackPointerMotion(e.Pointer.GetHashCode(), viewPoint);

        _inputProcessorAccessor().ProcessPointer(new PointerInput
        {
            EventType = PointerEventType.Pressed,
            Position = _normalizeCoordinates(viewPoint),
            PointerHash = e.Pointer.GetHashCode()
        });

        var shouldCapturePointer = _isMouseLockedAccessor() || e.GetCurrentPoint(_videoImage).Properties.IsLeftButtonPressed;
        if (shouldCapturePointer)
        {
            e.Pointer.Capture(_videoImage);
            _isPointerCaptured = true;
        }
    }

    private void VideoImage_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_isInputAvailableAccessor() || _videoImage == null)
        {
            return;
        }

        _activePointer = e.Pointer;
        var viewPoint = e.GetPosition(_videoImage);

        _inputProcessorAccessor().ProcessPointer(new PointerInput
        {
            EventType = PointerEventType.WheelChanged,
            Position = _normalizeCoordinates(viewPoint),
            WheelDelta = e.Delta
        });
        e.Handled = true;
    }

    private void VideoImage_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isInputAvailableAccessor() || _videoImage == null)
        {
            return;
        }

        _activePointer = e.Pointer;

        if (_isMouseLockedAccessor() && !_isPointerCaptured)
        {
            e.Pointer.Capture(_videoImage);
            _isPointerCaptured = true;
        }

        var viewPoint = e.GetPosition(_videoImage);
        var pointerHash = e.Pointer.GetHashCode();
        Vector? relativeDelta;
        var inputProcessor = _inputProcessorAccessor();

        if (inputProcessor is HidInputProcessor && !_isPointerCaptured)
        {
            ResetPointerMotionTracking();
            return;
        }

        if (_isMouseLockedAccessor() && inputProcessor is HidInputProcessor)
        {
            var centerPoint = new Point(_videoImage.Bounds.Width / 2, _videoImage.Bounds.Height / 2);
            if (_ignoreNextLockedPointerMove)
            {
                _ignoreNextLockedPointerMove = false;
                var warpDelta = viewPoint - centerPoint;
                if (Math.Abs(warpDelta.X) <= 1.5 && Math.Abs(warpDelta.Y) <= 1.5)
                {
                    TrackPointerMotion(pointerHash, centerPoint);
                    return;
                }
            }

            var deltaFromCenter = viewPoint - centerPoint;
            relativeDelta = (Math.Abs(deltaFromCenter.X) > 0.1 || Math.Abs(deltaFromCenter.Y) > 0.1)
                ? deltaFromCenter
                : null;

            TrackPointerMotion(pointerHash, centerPoint);

            if (relativeDelta.HasValue)
            {
                CenterCursorInVideoImage();
            }
        }
        else
        {
            relativeDelta = GetRelativePointerDelta(pointerHash, viewPoint);
        }

        inputProcessor.ProcessPointer(new PointerInput
        {
            EventType = PointerEventType.Moved,
            Position = _normalizeCoordinates(viewPoint),
            PointerHash = pointerHash,
            RelativeDelta = relativeDelta ?? default,
            HasRelativeDelta = relativeDelta.HasValue
        });
    }

    private void VideoImage_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerCaptured || !_isInputAvailableAccessor() || _videoImage == null)
        {
            return;
        }

        _activePointer = e.Pointer;
        var viewPoint = e.GetPosition(_videoImage);
        TrackPointerMotion(e.Pointer.GetHashCode(), viewPoint);

        _inputProcessorAccessor().ProcessPointer(new PointerInput
        {
            EventType = PointerEventType.Released,
            Position = _normalizeCoordinates(viewPoint),
            PointerHash = e.Pointer.GetHashCode()
        });

        if (!_isMouseLockedAccessor())
        {
            e.Pointer.Capture(null);
            _isPointerCaptured = false;
        }
    }

    private void VideoImage_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _activePointer = e.Pointer;

        _inputProcessorAccessor().ProcessPointer(new PointerInput
        {
            EventType = PointerEventType.CaptureLost,
            PointerHash = e.Pointer.GetHashCode()
        });

        e.Pointer.Capture(null);
        ResetPointerCapture();
        ResetPointerMotionTracking();
    }

    private void ResetPointerCapture()
    {
        _isPointerCaptured = false;
    }

    private static bool IsAltToggleKey(KeyEventArgs e)
    {
        return e.Key is Key.LeftAlt or Key.RightAlt
               || e.Key == Key.System
               || e.KeyModifiers.HasFlag(KeyModifiers.Alt)
               || e.PhysicalKey.ToString().Contains("Alt", StringComparison.OrdinalIgnoreCase);
    }

    private Vector? GetRelativePointerDelta(int pointerHash, Point currentViewPoint)
    {
        Vector? delta = null;

        if (_lastPointerHash == pointerHash && _lastPointerViewPoint is Point lastPoint)
        {
            delta = currentViewPoint - lastPoint;
        }

        TrackPointerMotion(pointerHash, currentViewPoint);
        return delta;
    }

    private void TrackPointerMotion(int pointerHash, Point currentViewPoint)
    {
        _lastPointerHash = pointerHash;
        _lastPointerViewPoint = currentViewPoint;
    }

    private void ResetPointerMotionTracking()
    {
        _lastPointerHash = null;
        _lastPointerViewPoint = null;
    }

    private void CenterCursorInVideoImage()
    {
        if (_videoImage == null)
        {
            return;
        }

        _ignoreNextLockedPointerMove = true;
        _mouseLockService.WarpCursorInTarget(
            new Point(_videoImage.Bounds.Width / 2, _videoImage.Bounds.Height / 2));
    }

    public void Dispose()
    {
        Detach();
    }
}
