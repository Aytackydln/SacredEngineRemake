using System.Buffers.Binary;

namespace Sacred.GameServer.Wire;

/// <summary>
/// Custom net opcodes ("net ops"): two wire opcodes above the stock range (the stock dispatchers end at type 0x1D5; the original
/// GameServer.exe logs and drops every id above it, no relay, no disconnect), carrying named mod channels between Sacred Gear
/// clients and the server's Lua scripts. Layouts (little endian, message payloads):
/// <code>
/// control (default 0x1E0)   [u8 kind] ...
///   1 HELLO      c-&gt;s  [u8 proto][u32 features][u32 mod-set hash]
///   2 HELLO_ACK  s-&gt;c  [u8 proto][u32 features][u16 data opcode][u16 max fragment][u32 max message][u32 rate bytes/s][u8 max in flight]
///   3 PEERS      s-&gt;c  [u16 mask]   bit n = slot n+1 can use net ops (sent when it changes)
/// data (default 0x1E1)      [u32 channel id = FNV-1a of the name][u8 flags][u8 target][u16 message id][bytes]
///   flags  bit0 first fragment, bit1 last fragment, bit2 JSON payload (else raw bytes)
///   target 0 server, 1 host, 0xFF everybody else, 0x11..0x20 slot 1..16 (0x10 + slot)
/// </code>
/// The server stamps the real sender slot into the message header (origin) and ignores any claim in the payload. A message is cut
/// into fragments of at most <see cref="HelloAck"/>.MaxFragment bytes; fragments of one message arrive in order (TCP / KCP).
/// </summary>
public static class NetOpsWire
{
    public const ushort DefaultControl = 0x1E0, DefaultData = 0x1E1;
    /// <summary>Highest stock message type (the original's dispatch tables end here); custom ids must be above it.</summary>
    public const ushort StockMax = 0x1D5;
    public const ushort IdLimit = 0x400;
    public const byte ProtocolVersion = 1;

    public const byte KindHello = 1, KindHelloAck = 2, KindPeers = 3;

    // Feature bits (HELLO: what the client can do, HELLO_ACK: what the server does).
    public const uint FeatureFragments = 1, FeatureJson = 2, FeatureRelay = 4, FeatureServerChannels = 8;

    public const byte FlagFirst = 1, FlagLast = 2, FlagJson = 4;
    public const byte TargetServer = 0, TargetHost = 1, TargetAll = 0xFF, TargetSlotBase = 0x10;

    public const int DataHeaderBytes = 8;
    public const int HelloBytes = 1 + 1 + 4 + 4, HelloAckBytes = 1 + 1 + 4 + 2 + 2 + 4 + 4 + 1, PeersBytes = 1 + 2;

    public static bool ValidIds(ushort control, ushort data) =>
        control > StockMax && data > StockMax && control < IdLimit && data < IdLimit && control != data;

    /// <summary>FNV-1a, 32 bit, over the UTF-8 bytes of the channel name.</summary>
    public static uint ChannelId(string name)
    {
        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(name)) hash = (hash ^ b) * 16777619u;
        return hash;
    }

    public static byte SlotTarget(int slot) => (byte)(TargetSlotBase + slot);
    public static bool IsSlotTarget(byte target) => target is > TargetSlotBase and <= TargetSlotBase + 16;

    public readonly record struct Hello(byte Protocol, uint Features, uint ModHash);
    public readonly record struct HelloAck(byte Protocol, uint Features, ushort DataOpcode, ushort MaxFragment, uint MaxMessage, uint BytesPerSecond, byte MaxInFlight);
    public readonly record struct DataHeader(uint Channel, byte Flags, byte Target, ushort MessageId)
    {
        public bool First => (Flags & FlagFirst) != 0;
        public bool Last => (Flags & FlagLast) != 0;
        public bool Json => (Flags & FlagJson) != 0;
    }

    public static bool TryReadHello(ReadOnlySpan<byte> payload, out Hello hello)
    {
        hello = default;
        if (payload.Length < HelloBytes || payload[0] != KindHello) return false;
        hello = new(payload[1], BinaryPrimitives.ReadUInt32LittleEndian(payload[2..]), BinaryPrimitives.ReadUInt32LittleEndian(payload[6..]));
        return true;
    }

    public static int WriteHello(Span<byte> d, Hello hello)
    {
        d[0] = KindHello; d[1] = hello.Protocol;
        BinaryPrimitives.WriteUInt32LittleEndian(d[2..], hello.Features);
        BinaryPrimitives.WriteUInt32LittleEndian(d[6..], hello.ModHash);
        return HelloBytes;
    }

    public static int WriteHelloAck(Span<byte> d, HelloAck ack)
    {
        d[0] = KindHelloAck; d[1] = ack.Protocol;
        BinaryPrimitives.WriteUInt32LittleEndian(d[2..], ack.Features);
        BinaryPrimitives.WriteUInt16LittleEndian(d[6..], ack.DataOpcode);
        BinaryPrimitives.WriteUInt16LittleEndian(d[8..], ack.MaxFragment);
        BinaryPrimitives.WriteUInt32LittleEndian(d[10..], ack.MaxMessage);
        BinaryPrimitives.WriteUInt32LittleEndian(d[14..], ack.BytesPerSecond);
        d[18] = ack.MaxInFlight;
        return HelloAckBytes;
    }

    public static bool TryReadHelloAck(ReadOnlySpan<byte> payload, out HelloAck ack)
    {
        ack = default;
        if (payload.Length < HelloAckBytes || payload[0] != KindHelloAck) return false;
        ack = new(payload[1], BinaryPrimitives.ReadUInt32LittleEndian(payload[2..]), BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]), BinaryPrimitives.ReadUInt32LittleEndian(payload[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[14..]), payload[18]);
        return true;
    }

    public static int WritePeers(Span<byte> d, ushort mask)
    {
        d[0] = KindPeers;
        BinaryPrimitives.WriteUInt16LittleEndian(d[1..], mask);
        return PeersBytes;
    }

    public static bool TryReadPeers(ReadOnlySpan<byte> payload, out ushort mask)
    {
        mask = 0;
        if (payload.Length < PeersBytes || payload[0] != KindPeers) return false;
        mask = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..]);
        return true;
    }

    public static bool TryReadData(ReadOnlySpan<byte> payload, out DataHeader header, out ReadOnlySpan<byte> data)
    {
        header = default; data = default;
        if (payload.Length < DataHeaderBytes) return false;
        header = new(BinaryPrimitives.ReadUInt32LittleEndian(payload), payload[4], payload[5], BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]));
        data = payload[DataHeaderBytes..];
        return true;
    }

    public static int WriteData(Span<byte> d, DataHeader header, ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(d, header.Channel);
        d[4] = header.Flags; d[5] = header.Target;
        BinaryPrimitives.WriteUInt16LittleEndian(d[6..], header.MessageId);
        data.CopyTo(d[DataHeaderBytes..]);
        return DataHeaderBytes + data.Length;
    }
}
