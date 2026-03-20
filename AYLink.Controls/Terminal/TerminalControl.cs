// Original source: Iciclecreek.Avalonia.Terminal (MIT License)
// Repository: https://github.com/tomlm/Iciclecreek.Avalonia.Terminal
// Copyright (c) Tom Laird-McConnell. All rights reserved.
// See Iciclecreek.Avalonia.Terminal.txt for license details.
// Modified for AYLink: removed PTY-dependent members, added external I/O API.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace AYLink.Controls.Terminal;

public class TerminalControl : TemplatedControl
{
    private TerminalView? _terminalView;
    private ScrollBar? _scrollBar;


    public static readonly StyledProperty<TextDecorationLocation?> TextDecorationsProperty =
        AvaloniaProperty.Register<TerminalControl, TextDecorationLocation?>(
            nameof(TextDecorations),
            defaultValue: null);

    public static readonly StyledProperty<IBrush> SelectionBrushProperty =
        AvaloniaProperty.Register<TerminalControl, IBrush>(
            nameof(SelectionBrush),
            defaultValue: new SolidColorBrush(Color.FromArgb(128, 0, 120, 215)));

    public static readonly StyledProperty<string> ProcessProperty =
        AvaloniaProperty.Register<TerminalControl, string>(
            nameof(Process),
            defaultValue: RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash");

    public static readonly StyledProperty<IList<string>> ArgsProperty =
        AvaloniaProperty.Register<TerminalControl, IList<string>>(
            nameof(Args),
            defaultValue: System.Array.Empty<string>());

    public static readonly StyledProperty<int> BufferSizeProperty =
              AvaloniaProperty.Register<TerminalControl, int>(
                  nameof(BufferSize),
                  defaultValue: 1000);

    public static readonly StyledProperty<XTerm.Options.TerminalOptions?> OptionsProperty =
        AvaloniaProperty.Register<TerminalControl, XTerm.Options.TerminalOptions?>(
            nameof(Options),
            defaultValue: null);

    public IBrush SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public string Process
    {
        get => GetValue(ProcessProperty);
        set => SetValue(ProcessProperty, value);
    }

    public IList<string> Args
    {
        get => GetValue(ArgsProperty);
        set => SetValue(ArgsProperty, value);
    }


    public int BufferSize
    {
        get => GetValue(BufferSizeProperty);
        set => SetValue(BufferSizeProperty, value);
    }
    
    public XTerm.Options.TerminalOptions? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    private static bool _stylesLoaded = false;

    static TerminalControl()
    {
        // Automatically load the default theme styles
        LoadDefaultStyles();

        // TerminalControl is focusable - it will delegate to inner TerminalView
        FocusableProperty.OverrideDefaultValue<TerminalControl>(true);
    }

    private static void LoadDefaultStyles()
    {
        if (_stylesLoaded || Application.Current == null)
            return;

        // Check if styles are already loaded to avoid duplicates
        foreach (var style in Application.Current.Styles)
        {
            if (style is Themes.Generic)
            {
                _stylesLoaded = true;
                return;
            }
        }

        var styles = (IStyle)new Themes.Generic();
        Application.Current.Styles.Add(styles);
        _stylesLoaded = true;
    }

    public TerminalControl()
    {
    }

    public XTerm.Terminal Terminal => _terminalView!.Terminal;

    /// <summary>
    /// Event raised when the terminal grid dimensions (rows/cols) change.
    /// Subscribe to synchronize remote shell PTY size via stty.
    /// </summary>
    public event EventHandler<TerminalSizeEventArgs>? TerminalResized;

    /// <summary>
    /// Writes data to the terminal emulator for display (e.g., output from ADB shell).
    /// The data is processed by XTerm.NET which handles VT escape sequences.
    /// </summary>
    public void WriteToTerminal(string data)
    {
        _terminalView?.WriteToTerminal(data);
    }

    /// <summary>
    /// Event raised when the user types input that should be sent to the connected device.
    /// Subscribe to this event to forward user keystrokes to ADB shell.
    /// Uses a backing event to safely handle subscription before template is applied.
    /// </summary>
    private event EventHandler<TerminalDataEventArgs>? _userInputBacking;
    private bool _userInputWired;

    public event EventHandler<TerminalDataEventArgs>? UserInput
    {
        add
        {
            _userInputBacking += value;
            if (_terminalView != null && !_userInputWired)
            {
                _terminalView.UserInput += OnTerminalViewUserInput;
                _userInputWired = true;
            }
        }
        remove
        {
            _userInputBacking -= value;
        }
    }

    private void OnTerminalViewUserInput(object? sender, TerminalDataEventArgs e)
    {
        _userInputBacking?.Invoke(this, e);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);

        // Only focus the inner TerminalView if it doesn't already have focus
        if (_terminalView != null && !_terminalView.IsFocused)
        {
            // Defer until layout is ready
            Dispatcher.UIThread.Post(() =>
            {
                if (_terminalView != null && !_terminalView.IsFocused)
                {
                    _terminalView.Focus();
                }
            }, DispatcherPriority.Input);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        // Ensure styles are loaded (handles case where static constructor ran before Application was ready)
        LoadDefaultStyles();

        base.OnApplyTemplate(e);

        // Unsubscribe from old controls
        if (_scrollBar != null)
        {
            _scrollBar.Scroll -= OnScrollBarScroll;
        }

        if (_terminalView != null)
        {
            _terminalView.PropertyChanged -= OnTerminalViewPropertyChanged;
            if (_userInputWired)
            {
                _terminalView.UserInput -= OnTerminalViewUserInput;
                _userInputWired = false;
            }
        }

        // Get template parts
        _terminalView = e.NameScope.Find<TerminalView>("PART_TerminalView");
        _scrollBar = e.NameScope.Find<ScrollBar>("PART_ScrollBar");

        // Wire up scrollbar and events
        if (_scrollBar != null && _terminalView != null)
        {
            _scrollBar.Scroll += OnScrollBarScroll;
            _terminalView.Options = Options ?? new XTerm.Options.TerminalOptions();
            _terminalView.PropertyChanged += OnTerminalViewPropertyChanged;

            // Wire up UserInput proxy if there are subscribers
            if (_userInputBacking != null && !_userInputWired)
            {
                _terminalView.UserInput += OnTerminalViewUserInput;
                _userInputWired = true;
            }
        }
    }

    private void OnScrollBarScroll(object? sender, ScrollEventArgs e)
    {
        if (_terminalView != null)
        {
            _terminalView.ViewportY = (int)e.NewValue;
        }
    }

    private void OnTerminalViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TerminalView.MaxScrollbackProperty ||
            e.Property == TerminalView.ViewportLinesProperty ||
            e.Property == TerminalView.ViewportYProperty ||
            e.Property == TerminalView.IsAlternateBufferProperty)
        {
            UpdateScrollBar();
        }

        // Detect terminal grid resize (rows/cols change)
        if (e.Property == TerminalView.ViewportLinesProperty && _terminalView != null)
        {
            var terminal = _terminalView.Terminal;
            TerminalResized?.Invoke(this, new TerminalSizeEventArgs(terminal.Cols, terminal.Rows));
        }
    }

    private void UpdateScrollBar()
    {
        if (_scrollBar == null || _terminalView == null)
            return;

        if (_terminalView.IsAlternateBuffer)
        {
            _scrollBar.IsVisible = false;
            _scrollBar.Value = 0;
            return;
        }

        var maxScrollback = _terminalView.MaxScrollback;
        var viewportLines = _terminalView.ViewportLines;
        var currentScroll = _terminalView.ViewportY;

        // Scrollbar range: 0 (top of buffer) to maxScrollback (bottom/current output)
        _scrollBar.Minimum = 0;
        _scrollBar.Maximum = maxScrollback;
        _scrollBar.ViewportSize = viewportLines;
        _scrollBar.Value = currentScroll;
        _scrollBar.IsVisible = maxScrollback > 0;
    }
}
