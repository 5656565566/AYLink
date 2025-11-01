using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using AYLink.UIModel;
using Avalonia.Controls;

namespace AYLink.Scrcpy;

public class ScrcpyClient : IDisposable
{
    private Socket? _videoSocket;
    private Socket? _audioSocket;
    private Socket? _controlSocket;

    private Thread? _videoThread;
    private Thread? _audioThread;
    private readonly Image _videoImage;

    private VideoDecoder? _videoDecoder;
    private AudioDecoder? _audioDecoder;

    private readonly DeviceModel _deviceModel;

    public volatile bool _isRunning;
    private int[] _ports = new int[3];

    public ScrcpyClient(Image videoImage, DeviceModel deviceModel)
    {
        _videoImage = videoImage;
        _deviceModel = deviceModel;

        InitializeSockets();
    }

    private void InitializeSockets()
    {
        _videoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _audioSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _controlSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    public bool Connect(int[] ports)
    {
        try
        {
            if (_isRunning) // 防止复用
            {
                DisConnect();
            }

            _ports = ports;

            ConnectVideo();
            ConnectAudio();
            ConnectControl();

            _isRunning = true;

            Debug.WriteLine("Connected to device successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Connection failed: {ex.Message}");
            return false;
        }
    }

    private void ConnectVideo()
    {
        if (_ports[0] == 0) { return; }

        _videoSocket!.Connect(new IPEndPoint(IPAddress.Loopback, _ports[0]));

        if (_videoSocket.Connected)
        {
            _deviceModel.VideoStream ??= [];
            _deviceModel.VideoStream.Add(_videoSocket);
        }

        _videoThread = new Thread(ReceiveVideoFrames);
        _videoThread.Start();
    }

    private void ConnectAudio()
    {
        if (_ports[1] == 0) { return; }

        _audioSocket!.Connect(new IPEndPoint(IPAddress.Loopback, _ports[1]));

        if (_audioSocket.Connected)
        {
            _deviceModel.AudioStream = _audioSocket;
        }

        _audioThread = new Thread(ReceiveAudioFrames);
        _audioThread.Start();
    }

    private void ConnectControl()
    {
        if (_ports[2] == 0) { return; }

        _controlSocket!.Connect(new IPEndPoint(IPAddress.Loopback, _ports[2]));

        if (_controlSocket.Connected)
        {
            _deviceModel.VideoStream ??= [];
            _deviceModel.VideoStream.Add(_controlSocket);
        }
    }

    private void ReceiveVideoFrames()
    {
        try
        {
            _videoDecoder = new VideoDecoder(_videoSocket!, _videoImage);
            _videoDecoder.InitializeFromStream();
            _videoDecoder.Start();
        }
        catch (Exception ex)
        {
            if (_isRunning)
            {
                _isRunning = false;
                Debug.WriteLine($"Video receive error: {ex.Message}");
            }
        }
    }
    private void ReceiveAudioFrames()
    {
        try
        {
            _audioDecoder = new(_audioSocket!, (_ports[0] == 0));
            _audioDecoder.Start();
        }
        catch (Exception ex)
        {
            if (_isRunning)
            {
                Debug.WriteLine($"Audio receive error: {ex.Message}");
            }
        }
    }

    public void SendControlCommand(byte[] data)
    {
        try
        {
            if (_controlSocket!.Connected)
            {
                _controlSocket.Send(data);
            }
            else
            {
                if (_isRunning)
                {
                    _controlSocket.Connect(new IPEndPoint(IPAddress.Loopback, _ports[2]));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Control command failed: {ex.Message}");
        }
    }


    public void DisConnect()
    {
        if (!_isRunning) return;

        _isRunning = false;

        SafeCloseSocket(_videoSocket);
        SafeCloseSocket(_audioSocket);
        SafeCloseSocket(_controlSocket);

        _videoThread?.Join(1000);
        _audioThread?.Join(1000);
    }

    private static void SafeCloseSocket(Socket? socket)
    {
        try
        {
            if (socket?.Connected == true)
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            socket?.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error closing socket: {ex.Message}");
        }
    }

    public void Dispose()
    {
        DisConnect();

        if (_audioSocket != null && _deviceModel.AudioStream == _audioSocket)
        {
            _deviceModel.AudioStream = null;
        }

        if (_deviceModel.VideoStream != null && _videoSocket != null)
        {
            _deviceModel.VideoStream.Remove(_videoSocket);
        }

        if (_deviceModel.ControlStream != null && _controlSocket != null)
        {
            _deviceModel.ControlStream.Remove(_controlSocket);
        }

        _videoSocket?.Dispose();
        _audioSocket?.Dispose();
        _controlSocket?.Dispose();

        _videoDecoder?.Dispose();
        _audioDecoder?.Dispose();

        GC.SuppressFinalize(this);
    }
}