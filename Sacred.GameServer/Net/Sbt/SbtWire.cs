using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Sacred.GameServer.Net.Sbt;

/// <summary>
/// Datagram layouts of Sacred Bild's UDP transport "SBT" (sacred-bild docs/UDP_PROTOCOL.md, src/net/udp_protocol.h),
/// little-endian, magic = "SBT" + type character, at most 1200 bytes:
/// HELLO 64 [version][clientNonce][u16 tcpPort][u16 0][cookie 16][0...], CHALLENGE 32 [version][nonce][0][cookie],
/// WELCOME 32 [version][nonce][sessionId][u64 token][0], REFUSED 16 [version][nonce][reason],
/// DATA/KEEPALIVE/CLOSE 16+n [sessionId][u64 token] (+ the KCP packet, conv = sessionId), PUNCH 16 (ignored).
/// </summary>
public static class SbtWire
{
    public const uint Version = 1;
    public const int MaxDatagram = 1200, HeaderBytes = 16, HelloBytes = 64, ChallengeBytes = 32, WelcomeBytes = 32, RefusedBytes = 16;
    public const int KcpMtu = MaxDatagram - HeaderBytes;      // 1184
    public const byte Hello = (byte)'H', Challenge = (byte)'C', Welcome = (byte)'W', Refused = (byte)'N', Data = (byte)'D',
        KeepAlive = (byte)'K', Close = (byte)'X', Punch = (byte)'P';
    public const uint RefusedDisabled = 1, RefusedNotListening = 2, RefusedFull = 3, RefusedVersion = 4;

    public static bool IsTransport(ReadOnlySpan<byte> d) => d.Length >= 4 && d[0] == 'S' && d[1] == 'B' && d[2] == 'T';

    public static void Magic(Span<byte> d, byte type) { d[0] = (byte)'S'; d[1] = (byte)'B'; d[2] = (byte)'T'; d[3] = type; }

    public static void SessionHeader(Span<byte> d, byte type, uint sessionId, ulong token)
    {
        Magic(d, type);
        BinaryPrimitives.WriteUInt32LittleEndian(d[4..], sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(d[8..], token);
    }
}

/// <summary>
/// Stateless handshake cookies: HMAC-SHA256(secret, address | port | nonce | 30 s period), first 16 bytes, valid for the
/// current and the previous period. Only this process checks them, so any keyed MAC works (Sacred Bild uses SipHash).
/// </summary>
public sealed class SbtCookies
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _input = new byte[16 + 2 + 4 + 8];
    private readonly byte[] _mac = new byte[32];

    public void Write(SocketAddress from, uint nonce, Span<byte> cookie) => Compute(from, nonce, Period(), cookie);

    public bool Valid(SocketAddress from, uint nonce, ReadOnlySpan<byte> cookie)
    {
        Span<byte> expected = stackalloc byte[16];
        var period = Period();
        Compute(from, nonce, period, expected);
        if (CryptographicOperations.FixedTimeEquals(expected, cookie)) return true;
        Compute(from, nonce, period - 1, expected);
        return CryptographicOperations.FixedTimeEquals(expected, cookie);
    }

    private static long Period() => Environment.TickCount64 / 30_000;

    private void Compute(SocketAddress from, uint nonce, long period, Span<byte> cookie)
    {
        var input = _input.AsSpan();
        input.Clear();
        AddressBytes(from, input[..18]);
        BinaryPrimitives.WriteUInt32LittleEndian(input[18..], nonce);
        BinaryPrimitives.WriteInt64LittleEndian(input[22..], period);
        HMACSHA256.HashData(_key, input, _mac);
        _mac.AsSpan(0, 16).CopyTo(cookie);
    }

    /// <summary>Family-independent form: 16-byte IPv6 (IPv4 mapped) + port. Reads the raw sockaddr without allocating.</summary>
    public static void AddressBytes(SocketAddress a, Span<byte> into)
    {
        var raw = a.Buffer.Span;
        if (a.Family == AddressFamily.InterNetwork)
        {
            into[10] = 0xFF; into[11] = 0xFF;
            raw.Slice(4, 4).CopyTo(into[12..]);       // sockaddr_in: family 2, port 2 (network order), address 4
        }
        else raw.Slice(8, 16).CopyTo(into);           // sockaddr_in6: family 2, port 2, flowinfo 4, address 16
        into[16] = raw[2]; into[17] = raw[3];
    }
}
