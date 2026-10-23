using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public enum GoldChatChannel { Public, Private, Team }

public sealed record GoldChannelChatEvent(GoldMessage Message, string Text) : GoldGameEvent(Message)
{
    public GoldChatChannel Channel => Message.Opcode switch
    {
        GoldChannelChatCodec.PrivateOpcode => GoldChatChannel.Private,
        GoldChannelChatCodec.TeamOpcode => GoldChatChannel.Team,
        _ => GoldChatChannel.Public
    };
    public bool IsServerMessage => Message.Origin == 0;
}

/// <summary>Gold 003/004/005 skip four bytes then read a terminated UTF-16 string; Origin selects the speaker.</summary>
public static class GoldChannelChatCodec
{
    public const ushort PrivateOpcode = 0x003;
    public const ushort TeamOpcode = 0x004;
    public const ushort PublicOpcode = 0x005;
    public static GoldChannelChatEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (PrivateOpcode or TeamOpcode or PublicOpcode) || message.Payload.Length < 6)
            throw new InvalidDataException("Gold channel chat requires four skipped bytes and terminated UTF-16 text.");
        var bytes = message.Payload.AsSpan(4);
        for (var offset = 0; offset + 1 < bytes.Length; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]) != 0) continue;
            var chars = new char[offset / 2];
            for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
            return new(message with { Payload = message.Payload.ToArray() }, new string(chars));
        }
        throw new InvalidDataException("Gold channel chat has an unterminated UTF-16 string.");
    }
}
