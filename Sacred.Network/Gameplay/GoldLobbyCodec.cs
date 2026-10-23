using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldLobbyEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldLobbyProfileEvent(GoldMessage Message, uint Value, string Text) : GoldLobbyEvent(Message);
public sealed record GoldLobbyWindowEvent(GoldMessage Message, uint Value, string? Text) : GoldLobbyEvent(Message)
{
    public uint NativeEventId => Message.Opcode == GoldLobbyCodec.WindowOpcode ? 0x36u : 0x38u;
    public string Target => Message.Opcode == GoldLobbyCodec.WindowOpcode ? "UI_WND_NETWORK" : "UI_NET_LBPLAYERS";
    public string Caption => Message.Opcode == GoldLobbyCodec.WindowOpcode ? "" : "NETMGR";
    public uint NativeMode => Message.Opcode == GoldLobbyCodec.WindowOpcode ? 1u : 3u;
    public uint NativeArgument => Message.Opcode == GoldLobbyCodec.WindowOpcode ? 0u : Value;
    public uint NativeFlags => Message.Opcode == GoldLobbyCodec.WindowOpcode ? 2u : 0u;
}
public sealed record GoldLobbyChannelsEvent(GoldMessage Message, IReadOnlyList<GoldLobbyChannelRow> Rows) : GoldLobbyEvent(Message);
public sealed record GoldLobbyChannelRow(uint UnreadWord, int PlayerCount, int PlayerLimit, uint Flags, ReadOnlyMemory<byte> NameBytes);

/// <summary>Gold lobby/profile notifications. No online-service requests are inferred from these receives.</summary>
public static class GoldLobbyCodec
{
    public const ushort ProfileOpcode = 0x027;
    public const ushort WindowOpcode = 0x02D;
    public const ushort ChannelsOpcode = 0x02E;
    public const ushort PlayerChangeOpcode = 0x02F;
    public const int ChannelStride = 176;

    public static GoldLobbyEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Payload.AsSpan();
        if (bytes.Length < 4) throw new InvalidDataException("Truncated Gold lobby header.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var owned = message with { Payload = bytes.ToArray() };
        switch (message.Opcode)
        {
            case ProfileOpcode: return new GoldLobbyProfileEvent(owned, value, ReadText(bytes, 4));
            case WindowOpcode: return new GoldLobbyWindowEvent(owned, value, null);
            case PlayerChangeOpcode: return new GoldLobbyWindowEvent(owned, value, ReadText(bytes, 4));
            case ChannelsOpcode:
                // Native reads the final name to NUL, not to the end of its nominal 176-byte record.
                if (value > 0 && (bytes.Length < 21 || value > 1u + (uint)(bytes.Length - 21) / ChannelStride))
                    throw new InvalidDataException("Truncated Gold channel list.");
                var rows = new List<GoldLobbyChannelRow>((int)value);
                for (var index = 0; index < value; index++)
                {
                    var start = 4 + index * ChannelStride;
                    var nameStart = start + 16;
                    var end = bytes[nameStart..].IndexOf((byte)0);
                    if (end < 0) throw new InvalidDataException("Unterminated Gold lobby channel name.");
                    rows.Add(new(BinaryPrimitives.ReadUInt32LittleEndian(bytes[start..]),
                        BinaryPrimitives.ReadInt32LittleEndian(bytes[(start + 4)..]),
                        BinaryPrimitives.ReadInt32LittleEndian(bytes[(start + 8)..]),
                        BinaryPrimitives.ReadUInt32LittleEndian(bytes[(start + 12)..]),
                        owned.Payload.AsMemory(nameStart, end)));
                }
                return new GoldLobbyChannelsEvent(owned, rows.AsReadOnly());
            default: throw new InvalidDataException("Not a supported Gold lobby notification.");
        }
    }

    internal static string ReadText(ReadOnlySpan<byte> bytes, int offset)
    {
        if (bytes.Length < offset) throw new InvalidDataException("Truncated Gold lobby text prefix.");
        var text = bytes[offset..];
        for (var end = 0; end + 1 < text.Length; end += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(text[end..]) != 0) continue;
            var chars = new char[end / 2];
            for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(text[(i * 2)..]);
            return new string(chars);
        }
        throw new InvalidDataException("Unterminated Gold lobby UTF-16 text.");
    }
}
