using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Sacred.GameServer.Config;

namespace Sacred.GameServer.Lan;

/// <summary>
/// Broadcasts the original UDP session announcement (4-byte header + 0xAE raw-transformed payload) so LAN clients list this
/// server. The datagram is built once into a fixed buffer and re-sent; only the player count byte is patched.
/// </summary>
public sealed class LanAnnouncer : IDisposable
{
    private const int Payload = 0xAE;
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly byte[] _plain = new byte[Payload];
    private readonly byte[] _datagram = new byte[4 + Payload];
    private readonly IPEndPoint _target;
    private byte _players = 255;

    public LanAnnouncer(GameServerConfig config, ushort version, IPAddress advertised)
    {
        _socket.EnableBroadcast = true;
        _target = new IPEndPoint(IPAddress.Broadcast, config.ListenPort);
        BinaryPrimitives.WriteUInt16LittleEndian(_plain, version);
        BinaryPrimitives.WriteUInt16LittleEndian(_plain.AsSpan(2), (ushort)config.TcpPort);
        var ip = advertised.GetAddressBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(_plain.AsSpan(4), (uint)(ip[0] << 24 | ip[1] << 16 | ip[2] << 8 | ip[3]));
        var flags = config.GameFlags();
        BinaryPrimitives.WriteUInt32LittleEndian(_plain.AsSpan(8), flags);
        _plain[13] = (byte)config.MaxPlayers;
        var name = config.ServerName.Length > 79 ? config.ServerName[..79] : config.ServerName;
        Encoding.Unicode.GetBytes(name, _plain.AsSpan(14, 160));
    }

    /// <summary>Send an announcement for the current player count (allocation free).</summary>
    public void Announce(int players)
    {
        if (_players != (byte)players)
        {
            _players = (byte)players;
            _plain[12] = _players;
            BinaryPrimitives.WriteUInt32LittleEndian(_datagram, Payload | 0x08000000u);
            var previous = (uint)Payload;
            for (var offset = 0; offset + 4 <= Payload; offset += 4)
            {
                var word = BinaryPrimitives.ReadUInt32LittleEndian(_plain.AsSpan(offset));
                var transformed = unchecked(previous + 0xB5D6C7A3) ^ word;
                BinaryPrimitives.WriteUInt32LittleEndian(_datagram.AsSpan(4 + offset), transformed);
                previous = transformed;
            }
            _plain.AsSpan(Payload - Payload % 4).CopyTo(_datagram.AsSpan(4 + Payload - Payload % 4));
        }
        try { _socket.SendTo(_datagram, _target); } catch (SocketException) { /* no route / interface down: retry next interval */ }
    }

    public void Dispose() => _socket.Dispose();
}
