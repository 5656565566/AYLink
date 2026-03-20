// Original source: Iciclecreek.Avalonia.Terminal (MIT License)
// Repository: https://github.com/tomlm/Iciclecreek.Avalonia.Terminal
// Copyright (c) Tom Laird-McConnell. All rights reserved.
// See Iciclecreek.Avalonia.Terminal.txt for license details.

using Avalonia.Interactivity;

namespace AYLink.Controls.Terminal;

/// <summary>
/// EventArgs for the ProcessExited event.
/// </summary>
public class ProcessExitedEventArgs : RoutedEventArgs
{
    public int ExitCode { get; }

    public ProcessExitedEventArgs(int exitCode)
    {
        ExitCode = exitCode;
    }
}
