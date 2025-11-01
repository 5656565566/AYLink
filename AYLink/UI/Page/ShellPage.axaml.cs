using AdvancedSharpAdbClient;
using AYLink.UI.Themes;
using AYLink.UIModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.UI;

public partial class ShellPage : UserControl
{
    private readonly BackgroundImageManager backgroundImageManager = BackgroundImageManager.Instance;

    private CancellationTokenSource? _sessionCancellationTokenSource;
    private IAdbSocket? _persistentSocket;
    private bool _shellSessionActive = false;
    private DeviceModel? _deviceModel;

    public ShellPage()
    {
        InitializeComponent();
        backgroundImageManager.RegisterImageComponent(BackgroundImage);
        backgroundImageManager.SetRandomBackgroundImage();

        WriteOutput(L.Tr("ShellPage_Welcome_NoDevice"));
    }

    public void Dispose()
    {
        CloseShellSession();
    }

    private void UpdateWelcomeMessage()
    {
        ClearOutput();
        if (_deviceModel == null)
        {
            WriteOutput(L.Tr("ShellPage_Info_NoDeviceConnected"));
        }
        else
        {
            WriteOutput(L.Tr("ShellPage_Info_ConnectedTo", _deviceModel.Name, _deviceModel.Serial));
        }
        WriteOutput(L.Tr("ShellPage_Info_HelpHint"));
    }

    public async void SelectDevice(DeviceModel deviceModel)
    {
        if (_deviceModel != null)
        {
            CloseShellSession();
        }
        _deviceModel = deviceModel;
        UpdateWelcomeMessage();
        await StartShellSession();
    }

    private void CloseShellSession()
    {
        if (_shellSessionActive)
        {
            _sessionCancellationTokenSource?.Cancel();
            _persistentSocket?.Dispose();
            _sessionCancellationTokenSource?.Dispose();

            _shellSessionActive = false;
            _persistentSocket = null;
            _sessionCancellationTokenSource = null;
        }
    }

    private async Task StartShellSession()
    {
        if (_deviceModel == null || _shellSessionActive) return;

        try
        {
            var adbClient = (AdbClient)AdbClient.Instance;
            _persistentSocket = adbClient.CreateAdbSocket();
            await _persistentSocket.SetDeviceAsync(_deviceModel.DeviceData, CancellationToken.None);
            await _persistentSocket.SendAdbRequestAsync("shell:", CancellationToken.None);
            await _persistentSocket.ReadAdbResponseAsync(CancellationToken.None);

            _shellSessionActive = true;
            _sessionCancellationTokenSource = new CancellationTokenSource();

            _ = Task.Run(() => ReadShellOutputAsync(_sessionCancellationTokenSource.Token));

            WriteOutput(L.Tr("ShellPage_Info_SessionStarted"));
        }
        catch (Exception ex)
        {
            WriteOutput(L.Tr("ShellPage_Error_SessionStartFailed", ex.Message));
            CloseShellSession();
        }
    }

    private async Task ReadShellOutputAsync(CancellationToken token)
    {
        if (_persistentSocket == null) return;

        try
        {
            using var stream = _persistentSocket.GetShellStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);

            var buffer = new char[4096];
            while (!token.IsCancellationRequested && _persistentSocket.Connected)
            {
                int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead > 0)
                {
                    var output = new string(buffer, 0, charsRead);
                    WriteOutput(output, addNewLine: false);
                }
                else
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            WriteOutput(L.Tr("ShellPage_Error_Connection", ex.Message));
        }
        finally
        {
            _shellSessionActive = false;
            WriteOutput(L.Tr("ShellPage_Info_SessionTerminated"));
        }
    }

    private async Task SendCommandToShell(string command)
    {
        if (_persistentSocket == null || !_shellSessionActive)
        {
            WriteOutput(L.Tr("ShellPage_Error_SessionInactive"));
            return;
        }

        byte[] commandBytes = Encoding.UTF8.GetBytes(command + "\n");
        await _persistentSocket.SendAsync(commandBytes, CancellationToken.None);
    }

    private async Task SendCtrlCAsync()
    {
        if (_persistentSocket != null && _shellSessionActive)
        {
            byte[] ctrlC = { 3 };
            await _persistentSocket.SendAsync(ctrlC, CancellationToken.None);
        }
    }

    private async void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;

            string? command = TerminalInput.Text?.Trim();
            TerminalInput.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            await ProcessCommandAsync(command);
        }
    }

    private async Task ProcessCommandAsync(string command)
    {
        if (command.Equals("/help", StringComparison.CurrentCultureIgnoreCase))
        {
            WriteOutput(L.Tr("ShellPage_Help_Help"));
            WriteOutput(L.Tr("ShellPage_Help_Echo"));
            WriteOutput(L.Tr("ShellPage_Help_Clear"));
            WriteOutput(L.Tr("ShellPage_Help_Exit"));
            WriteOutput(L.Tr("ShellPage_Help_Stop"));
        }
        else if (command.StartsWith("/echo", StringComparison.CurrentCultureIgnoreCase))
        {
            WriteOutput(command.Length > 5 ? command[5..].TrimStart() : "");
        }
        else if (command.Equals("/clear", StringComparison.CurrentCultureIgnoreCase))
        {
            ClearOutput();
        }
        else if (command.Equals("/stop", StringComparison.CurrentCultureIgnoreCase))
        {
            WriteOutput(L.Tr("ShellPage_Info_SendingCtrlC"));
            await SendCtrlCAsync();
        }
        else if (command.Equals("/exit", StringComparison.CurrentCultureIgnoreCase))
        {
            if (_deviceModel == null)
            {
                WriteOutput(L.Tr("ShellPage_Error_NoDeviceBound"));
            }
            else
            {
                WriteOutput(L.Tr("ShellPage_Info_Disconnecting", _deviceModel.Name));
                CloseShellSession();
                _deviceModel = null;
                UpdateWelcomeMessage();
            }
        }
        else
        {
            if (_deviceModel == null)
            {
                WriteOutput(L.Tr("ShellPage_Error_NoDeviceToCommand"));
                return;
            }

            if (!_shellSessionActive)
            {
                WriteOutput(L.Tr("ShellPage_Info_Reconnecting"));
                await StartShellSession();
            }

            if (_shellSessionActive)
            {
                await SendCommandToShell(command);
            }
        }
    }

    public void WriteOutput(string text)
    {
        WriteOutput(text, true);
    }

    public void WriteOutput(string text, bool addNewLine)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            TerminalOutput.Text += text;
            if (addNewLine)
            {
                TerminalOutput.Text += Environment.NewLine;
            }
        });
    }

    public void ClearOutput()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            TerminalOutput.Text = string.Empty;
        });
    }
}