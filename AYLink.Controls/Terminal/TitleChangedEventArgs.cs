// Original source: Iciclecreek.Avalonia.Terminal (MIT License)
// Copyright (c) Tom Laird-McConnell. All rights reserved.
// See docs/licenses/Iciclecreek.Avalonia.Terminal.txt for license details.

using Avalonia.Interactivity;

namespace AYLink.Controls.Terminal;

/// <summary>
/// EventArgs for the TitleChanged event.
/// </summary>
public class TitleChangedEventArgs : RoutedEventArgs
{
    public string Title { get; }

    public TitleChangedEventArgs(string title)
    {
        Title = title;
    }
}
