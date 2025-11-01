using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace AYLink.Utils;

internal class PortUtility
{
    // 查询端口是否被占用，并查看占用程序
    public static bool IsPortInUse(int port)
    {
        IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
        IPEndPoint[] tcpEndPoints = properties.GetActiveTcpListeners();
        IPEndPoint[] udpEndPoints = properties.GetActiveUdpListeners();
        TcpConnectionInformation[] tcpConnections = properties.GetActiveTcpConnections();

        foreach (IPEndPoint endPoint in tcpEndPoints)
        {
            if (endPoint.Port == port)
            {
                Debug.WriteLine($"Port {port} is being used by TCP protocol.");
                return true;
            }
        }

        foreach (IPEndPoint endPoint in udpEndPoints)
        {
            if (endPoint.Port == port)
            {
                Debug.WriteLine($"Port {port} is being used by UDP protocol.");
                return true;
            }
        }

        foreach (TcpConnectionInformation tcpConnection in tcpConnections)
        {
            if (tcpConnection.LocalEndPoint.Port == port)
            {
                Debug.WriteLine($"Port {port} is being used by TCP connection.");
                return true;
            }
        }

        return false;
    }

    // 返回指定范围内的随机未被占用端口
    public static int GetRandomUnusedPort(int minPort, int maxPort)
    {
        Random random = new();
        int port;
        do
        {
            port = random.Next(minPort, maxPort + 1);
        } while (IsPortInUse(port));

        return port;
    }
}