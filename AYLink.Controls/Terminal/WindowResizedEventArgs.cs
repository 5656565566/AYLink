// Original source: Iciclecreek.Avalonia.Terminal (MIT License)
// Copyright (c) Tom Laird-McConnell. All rights reserved.
// See docs/licenses/Iciclecreek.Avalonia.Terminal.txt for license details.

using Avalonia.Interactivity;

namespace AYLink.Controls.Terminal;

/// <summary>
/// EventArgs for the WindowResized event.
/// </summary>
public class WindowResizedEventArgs : RoutedEventArgs
{
    public int Width { get; }
    public int Height { get; }

    public WindowResizedEventArgs(int width, int height)
    {
        Width = width;
        Height = height;
    }
}
