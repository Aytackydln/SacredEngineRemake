namespace Sacred.GameServer.Wire;

/// <summary>
/// The chat carrier of Sacred Gear's net channels: a message (bytes) cut into private chat (0x003) frames of at most 127 UTF-16 units
/// that every game server relays and no player sees. Units: <c>[0xE1A0][0x4000 + message number][0x4000 + frame index]</c>, in the first
/// frame the total length in two more units (<c>0x4000 + hi</c>, <c>0x4000 + lo</c>, 15 bits each), then the bytes packed 15 bits per
/// unit, each unit <c>0x4000 + value</c> (no NUL, no surrogates). 228 bytes fit the first frame, 232 each further one. A message is
/// <c>[u8 kind][u8 name length][name][body]</c>: kind <c>n</c> / <c>k</c> a net channel message (bytes / JSON), <c>q</c> the server's
/// query and <c>a</c> the client's answer of the handshake (name <c>gear.net</c>; <c>h</c> is the client's announcement that starts it: a server never writes chat frames to players that did not announce, so plain clients never see them), <c>s</c> / <c>j</c> the <c>gear.mp</c> messages
/// between clients (relayed, not interpreted here). Handshake bodies: <c>h</c> [u8 protocol]; <c>q</c> [u8 protocol][u16 control][u16 data]; <c>a</c> the same
/// plus [u8 flags: 1 accepts the custom message types].
/// </summary>
public static class NetChatWire
{
    public const ushort Marker = 0xE1A0;
    public const int MaxUnits = 127, FirstBytes = 228, NextBytes = 232, MaxMessage = 65536 + 80;
    public const byte KindBytes = (byte)'n', KindJson = (byte)'k', KindQuery = (byte)'q', KindAnswer = (byte)'a', KindHello = (byte)'h';
    public const string HandshakeName = "gear.net";

    public static int FrameCount(int length) => length <= FirstBytes ? 1 : 1 + (length - FirstBytes + NextBytes - 1) / NextBytes;

    /// <summary>The frames (UTF-16LE text without the terminating NUL) of one message.</summary>
    public static List<byte[]> Encode(ReadOnlySpan<byte> message, int number)
    {
        var frames = new List<byte[]>(FrameCount(message.Length));
        var position = 0;
        for (var index = 0; index == 0 || position < message.Length; index++)
        {
            var units = new List<ushort>(MaxUnits) { Marker, (ushort)(0x4000 + number), (ushort)(0x4000 + index) };
            var size = Math.Min(index == 0 ? FirstBytes : NextBytes, message.Length - position);
            if (index == 0) { units.Add((ushort)(0x4000 + (message.Length >> 15))); units.Add((ushort)(0x4000 + (message.Length & 0x7FFF))); }
            Pack(message.Slice(position, size), units);
            position += size;
            var bytes = new byte[units.Count * 2];
            for (var i = 0; i < units.Count; i++) { bytes[i * 2] = (byte)units[i]; bytes[i * 2 + 1] = (byte)(units[i] >> 8); }
            frames.Add(bytes);
        }
        return frames;
    }

    private static void Pack(ReadOnlySpan<byte> data, List<ushort> units)
    {
        uint accumulator = 0; var bits = 0;
        foreach (var b in data)
        {
            accumulator = (accumulator << 8) | b; bits += 8;
            if (bits >= 15) { bits -= 15; units.Add((ushort)(0x4000 + ((accumulator >> bits) & 0x7FFF))); accumulator &= (1u << bits) - 1; }
        }
        if (bits > 0) units.Add((ushort)(0x4000 + ((accumulator << (15 - bits)) & 0x7FFF)));
    }

    /// <summary>One parsed frame: message number, frame index, the total length (first frame only) and the packed units.</summary>
    public readonly record struct Frame(int Number, int Index, int Length, ushort[] Packed);

    /// <summary>Reads the text of a chat message (UTF-16LE, up to the first NUL) as a frame; false when it is not one of ours.</summary>
    public static bool TryRead(ReadOnlySpan<byte> text, out Frame frame)
    {
        frame = default;
        var count = 0;
        var units = new ushort[Math.Min(MaxUnits, text.Length / 2)];
        while (count < units.Length)
        {
            var unit = (ushort)(text[count * 2] | (text[count * 2 + 1] << 8));
            if (unit == 0) break;
            units[count++] = unit;
        }
        if (count < 4 || units[0] != Marker) return false;
        for (var i = 1; i < count; i++) if (units[i] < 0x4000 || units[i] > 0xBFFF) return false;
        var number = units[1] - 0x4000; var index = units[2] - 0x4000;
        if (index == 0)
        {
            if (count < 6) return false;
            frame = new(number, 0, (units[3] - 0x4000) * 0x8000 + (units[4] - 0x4000), units[5..count]);
        }
        else frame = new(number, index, 0, units[3..count]);
        return true;
    }

    /// <summary>Bytes a frame of a message of <paramref name="total"/> bytes carries at <paramref name="index"/>.</summary>
    public static int BytesIn(int total, int index) =>
        index == 0 ? Math.Min(total, FirstBytes) : Math.Max(0, Math.Min(total - FirstBytes - (index - 1) * NextBytes, NextBytes));

    /// <summary>Unpacks <paramref name="count"/> bytes; false when the units run out.</summary>
    public static bool Unpack(ReadOnlySpan<ushort> units, int count, List<byte> destination)
    {
        uint accumulator = 0; var bits = 0; var got = 0;
        foreach (var unit in units)
        {
            if (got >= count) break;
            accumulator = (accumulator << 15) | (uint)(unit - 0x4000); bits += 15;
            while (bits >= 8 && got < count) { bits -= 8; got++; destination.Add((byte)((accumulator >> bits) & 0xFF)); }
            accumulator &= (1u << bits) - 1;
        }
        return got == count;
    }

    /// <summary>Splits a complete message into kind, name and body.</summary>
    public static bool TrySplit(ReadOnlySpan<byte> message, out byte kind, out string name, out ReadOnlySpan<byte> body)
    {
        kind = 0; name = ""; body = default;
        if (message.Length < 2 || message[1] == 0 || message.Length < 2 + message[1]) return false;
        kind = message[0];
        name = System.Text.Encoding.Latin1.GetString(message.Slice(2, message[1]));
        body = message[(2 + message[1])..];
        return true;
    }

    public static byte[] Join(byte kind, string name, ReadOnlySpan<byte> body)
    {
        var bytes = new byte[2 + name.Length + body.Length];
        bytes[0] = kind; bytes[1] = (byte)name.Length;
        System.Text.Encoding.Latin1.GetBytes(name, bytes.AsSpan(2));
        body.CopyTo(bytes.AsSpan(2 + name.Length));
        return bytes;
    }
}
