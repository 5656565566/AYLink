// Original source: Iciclecreek.Avalonia.Terminal (MIT License)
// Copyright (c) Tom Laird-McConnell. All rights reserved.
// See docs/licenses/Iciclecreek.Avalonia.Terminal.txt for license details.

using Avalonia.Interactivity;

namespace AYLink.Controls.Terminal;

/// <summary>
/// EventArgs for the WindowMoved event.
/// </summary>
public class WindowMovedEventArgs : RoutedEventArgs
{
    public int X { get; }
    public int Y { get; }

    public WindowMovedEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }
}
