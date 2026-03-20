// Original source: Iciclecreek.Avalonia.Terminal (MIT License)
// Repository: https://github.com/tomlm/Iciclecreek.Avalonia.Terminal
// Copyright (c) Tom Laird-McConnell. All rights reserved.
// See Iciclecreek.Avalonia.Terminal.txt for license details.
// Modified for AYLink: added for terminal resize notification.


namespace AYLink.Controls.Terminal;

/// <summary>
/// EventArgs for terminal grid resize events (rows/cols change).
/// Used to synchronize remote shell PTY size.
/// </summary>
public class TerminalSizeEventArgs : EventArgs
{
    public int Cols { get; }
    public int Rows { get; }

    public TerminalSizeEventArgs(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
    }
}
