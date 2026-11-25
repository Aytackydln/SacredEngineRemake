using System.Net;
using System.Net.Sockets;

namespace Sacred.GameServer.Net;

/// <summary>
/// Which address the listeners bind. The default is the original's: every interface (dual-stack when IPv6 exists). <c>--bind=127.0.0.1</c>
/// (config key BIND_ADDRESS) binds one address only, no dual-stack socket and no wildcard, which keeps tests and manual checks out of
/// the firewall prompts. <see cref="Default"/> is what a listener uses when it is not told an address; the test project sets it to
/// loopback so no test can bind a wildcard by accident.
/// </summary>
public static class BindPolicy
{
    /// <summary>null = every interface.</summary>
    public static IPAddress? Default { get; set; }

    public static bool IsLoopback(IPAddress? address) => address is not null && IPAddress.IsLoopback(address);

    /// <summary>The socket and local end point for a listener: a plain socket of the address' family, or the dual-stack wildcard.</summary>
    public static (Socket Socket, IPEndPoint EndPoint) Create(SocketType type, ProtocolType protocol, IPAddress? bind, int port)
    {
        bind ??= Default;
        if (bind is not null) return (new Socket(bind.AddressFamily, type, protocol), new IPEndPoint(bind, port));
        var v6 = Socket.OSSupportsIPv6;
        var socket = new Socket(v6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, type, protocol);
        if (v6) socket.DualMode = true;
        return (socket, new IPEndPoint(v6 ? IPAddress.IPv6Any : IPAddress.Any, port));
    }
}
