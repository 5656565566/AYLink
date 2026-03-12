// Original source: Iciclecreek.Avalonia.Terminal (MIT License)
// See docs/licenses/Iciclecreek.Avalonia.Terminal.txt for license details.

namespace AYLink.Controls.Terminal;

/// <summary>
/// Event args for terminal data events (user input that should be forwarded to the connected device).
/// </summary>
public class TerminalDataEventArgs : EventArgs
{
    public TerminalDataEventArgs(string data)
    {
        Data = data;
    }

    /// <summary>
    /// The data string (keystrokes, escape sequences) to be sent to the connected device.
    /// </summary>
    public string Data { get; }
}
