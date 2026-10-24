using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold's 200-byte lobby server record. Unmapped words keep their wire offsets as names.</summary>
public sealed record GoldLobbyServerRecord(IReadOnlyList<uint> Words, ReadOnlyMemory<byte> NameBytes,
    uint EntryKey, uint WindowValue, string? UnicodeName = null);
public sealed record GoldLobbyServerEvent(GoldMessage Message, GoldLobbyServerRecord Server) : GoldLobbyEvent(Message);
public sealed record GoldLobbyPlayerRefreshEvent(GoldMessage Message, uint Value, string Text) : GoldLobbyEvent(Message)
{
    public uint NativeEventId => 0x38;
    public string Target => "UI_NET_LBPLAYERS";
    public string Caption => "NETMGR";
    public uint NativeMode => 2;
    public uint NativeArgument => Value;
    public uint NativeFlags => 0;
}

/// <summary>Gold receive 7E264E/7E28F0/7E2C3F/7E3C21. Browser names use the process ANSI locale; 047 uses UTF-16.</summary>
public static class GoldLobbyServerCodec
{
    public const ushort UpsertOpcode = 0x02A;
    public const ushort RemoveOpcode = 0x02C;
    public const ushort PlayerRefreshOpcode = 0x030;
    public const ushort CurrentOpcode = 0x047;
    public const int RecordSize = 200;
    public const int NameOffset = 32;
    public const int EntryKeyOffset = 192;
    public const int WindowValueOffset = 196;

    public static GoldLobbyEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Payload.AsSpan();
        var owned = message with { Payload = bytes.ToArray() };
        if (message.Opcode == PlayerRefreshOpcode)
        {
            var text = GoldLobbyCodec.ReadText(bytes, 4);
            return new GoldLobbyPlayerRefreshEvent(owned, BinaryPrimitives.ReadUInt32LittleEndian(bytes), text);
        }
        if (message.Opcode is not (UpsertOpcode or RemoveOpcode or CurrentOpcode))
            throw new InvalidDataException("Not a Gold lobby server record.");
        // Removal reads only word zero and the NUL-terminated ANSI name; the other branches read both tail words.
        var minimum = message.Opcode == RemoveOpcode ? NameOffset + 1 : RecordSize;
        if (bytes.Length < minimum) throw new InvalidDataException("Truncated Gold lobby server record.");
        // 047 uses the wide-string helper; 02A/02C use MultiByteToWideChar(CP_ACP).
        var unicodeName = message.Opcode == CurrentOpcode ? GoldLobbyCodec.ReadText(bytes, NameOffset) : null;
        var end = unicodeName is null ? bytes[NameOffset..].IndexOf((byte)0) : unicodeName.Length * 2;
        if (end < 0) throw new InvalidDataException("Unterminated Gold lobby server name.");
        var words = new uint[8];
        for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
        return new GoldLobbyServerEvent(owned, new(Array.AsReadOnly(words), owned.Payload.AsMemory(NameOffset, end),
            message.Opcode == RemoveOpcode ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(bytes[EntryKeyOffset..]),
            message.Opcode == RemoveOpcode ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(bytes[WindowValueOffset..]), unicodeName));
    }
}
