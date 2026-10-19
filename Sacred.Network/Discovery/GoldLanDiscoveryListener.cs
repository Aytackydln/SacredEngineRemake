using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Sacred.Network.Discovery;

/// <summary>Passive IPv4 broadcast receiver, polled without blocking the game thread.</summary>
public sealed class GoldLanDiscoveryListener : IDisposable
{
    private readonly Socket _socket;
    private readonly byte[] _datagram = new byte[ushort.MaxValue];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly GoldLanServerCatalog _catalog = new();
    public IReadOnlyList<GoldLanAnnouncement> Servers => _catalog.Servers;
    public int Port { get; }
    public int RejectedDatagrams { get; private set; }

    public GoldLanDiscoveryListener(int port)
    {
        if (port is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port;
        _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // The original client may already be listening on the same broadcast port.
            _socket.ExclusiveAddressUse = false;
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            _socket.Blocking = false;
        }
        catch { _socket.Dispose(); throw; }
    }

    public bool Update()
    {
        var changed = false;
        // Bound per-frame work even on a busy LAN; unread datagrams stay queued for the next frame.
        for (var i = 0; i < 64 && _socket.Poll(0, SelectMode.SelectRead); i++)
        {
            int size;
            try { size = _socket.Receive(_datagram); }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.WouldBlock) { break; }
            try { changed |= _catalog.Apply(GoldLanAnnouncementCodec.Decode(_datagram.AsSpan(0, size)), _clock.Elapsed); }
            catch (InvalidDataException) { RejectedDatagrams++; }
        }
        return _catalog.Expire(_clock.Elapsed) || changed;
    }

    public void Dispose() => _socket.Dispose();
}
