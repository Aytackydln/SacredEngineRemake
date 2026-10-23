using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldSessionNoticeEvent(GoldMessage Message, string? Text, byte? ModalFlag) : GoldGameEvent(Message)
{
    public bool ClearsBusyDialog => Message.Opcode != GoldSessionNoticeCodec.BuddyAddedOpcode;
    public bool IsModal => Message.Opcode != GoldSessionNoticeCodec.BuddyAddedOpcode &&
        (Message.Opcode != GoldSessionNoticeCodec.LobbyNoticeOpcode || ModalFlag is > 0);
    public bool UpdatesCustomText => Message.Opcode is GoldSessionNoticeCodec.ConnectionFailedOpcode or
        GoldSessionNoticeCodec.ServerNoticeOpcode or GoldSessionNoticeCodec.LobbyNoticeOpcode or GoldSessionNoticeCodec.LobbyKickOpcode;
    public string? LocalizationKey => Message.Opcode switch
    {
        GoldSessionNoticeCodec.LobbyKickOpcode => "UI_NET_LOBBYKICK",
        GoldSessionNoticeCodec.BuddyAddedOpcode => "UI_ERROR_BUDDYLIST_ADD",
        GoldSessionNoticeCodec.ConnectionFailedOpcode => "ERROR_TINCAT_CONNECT_FAILED",
        GoldSessionNoticeCodec.ConnectionAbortedOpcode => "ERROR_TINCAT_CONNECTABORT",
        _ => null
    };
}

/// <summary>Gold session/lobby notices; skipped prefixes have no invented meaning.</summary>
public static class GoldSessionNoticeCodec
{
    public const ushort LobbyKickOpcode = 0x031;
    public const ushort BuddyAddedOpcode = 0x035;
    public const ushort ConnectionFailedOpcode = 0x038;
    public const ushort ConnectionAbortedOpcode = 0x03A;
    public const ushort ServerNoticeOpcode = 0x03C;
    public const ushort LobbyNoticeOpcode = 0x03D;

    public static GoldSessionNoticeEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var offset = message.Opcode switch
        {
            LobbyKickOpcode => 8, BuddyAddedOpcode => 12, ConnectionFailedOpcode or LobbyNoticeOpcode => 6,
            ServerNoticeOpcode => 2, ConnectionAbortedOpcode => -1,
            _ => throw new InvalidDataException("Not a Gold session/lobby notice.")
        };
        if (offset < 0) return new(message with { Payload = message.Payload.ToArray() }, null, null);
        var bytes = message.Payload.AsSpan();
        if (bytes.Length < offset + 2) throw new InvalidDataException("Truncated Gold session notice text.");
        var text = bytes[offset..];
        for (var end = 0; end + 1 < text.Length; end += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(text[end..]) != 0) continue;
            var chars = new char[end / 2];
            for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(text[(i * 2)..]);
            return new(message with { Payload = bytes.ToArray() }, new string(chars), message.Opcode == LobbyNoticeOpcode ? bytes[4] : null);
        }
        throw new InvalidDataException("Unterminated Gold session notice UTF-16 text.");
    }
}
