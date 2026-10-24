using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldBuddyRecord(uint Id, uint Status, string Name, uint GameWord, string GameName, int ChannelIndex, string ChannelName);
public sealed record GoldBuddyEvent(GoldMessage Message, GoldBuddyRecord Buddy) : GoldLobbyEvent(Message);

/// <summary>Gold buddy fields at +4/+8/+C/+10C/+110/+210/+214. UTF-16 code units are preserved exactly.</summary>
public static class GoldBuddyCodec
{
    public const ushort QueryOpcode = 0x033;
    public const ushort UpdateOpcode = 0x036;
    public static GoldBuddyEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (QueryOpcode or UpdateOpcode)) throw new InvalidDataException("Not a Gold buddy packet.");
        var bytes = message.Payload.AsSpan();
        if (bytes.Length < 12) throw new InvalidDataException("Truncated Gold buddy header.");
        var id = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var status = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        uint game = 0; int channel = -1; string name = "", gameName = "", channelName = "";
        // 033 unknown statuses issue the already-empty native string without reading any name.
        if (message.Opcode == UpdateOpcode || id == uint.MaxValue || status <= 2) name = Text(bytes, 12, 0x10C);
        if (message.Opcode == UpdateOpcode || id != uint.MaxValue && status == 2)
        {
            if (bytes.Length < 0x110) throw new InvalidDataException("Truncated Gold buddy game field.");
            game = BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x10C..]);
        }
        if (message.Opcode == UpdateOpcode || id != uint.MaxValue && (status == 1 || status == 2 && game != 0))
        {
            if (bytes.Length < 0x214) throw new InvalidDataException("Truncated Gold buddy channel field.");
            channel = BinaryPrimitives.ReadInt32LittleEndian(bytes[0x210..]);
        }
        if (message.Opcode == UpdateOpcode || id != uint.MaxValue && status == 2 && game != 0)
            gameName = Text(bytes, 0x110, 0x210);
        if (message.Opcode == UpdateOpcode || id != uint.MaxValue && (status == 1 && channel >= 0 || status == 2 && game != 0))
            channelName = Text(bytes, 0x214, bytes.Length);
        return new(message with { Payload = bytes.ToArray() }, new(id, status, name, game, gameName, channel, channelName));
    }
    private static string Text(ReadOnlySpan<byte> bytes, int start, int limit)
    {
        var end = Math.Min(limit, bytes.Length);
        for (var offset = start; offset + 1 < end; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]) != 0) continue;
            var chars = new char[(offset - start) / 2];
            for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(start + 2 * i)..]);
            return new string(chars);
        }
        throw new InvalidDataException("Unterminated Gold buddy text field.");
    }
}
