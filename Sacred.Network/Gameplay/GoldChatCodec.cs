using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldChatEvent(GoldMessage Message, uint Speaker, int Recipient, string? Name, string Text) : GoldGameEvent(Message)
{
    public bool IsServerMessage => Speaker == 0;
    public bool IsPrivate => Recipient > 0;
}

/// <summary>Gold 032: two routing words, a 256-byte UTF-16 name area and NUL-terminated UTF-16 text.</summary>
public static class GoldChatCodec
{
    public const ushort Opcode = 0x032;
    public const int NameOffset = 8;
    public const int TextOffset = 264;
    public static GoldChatEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < TextOffset + 2)
            throw new InvalidDataException("Gold chat requires its routing words, name area and terminated UTF-16 text.");
        var bytes = message.Payload.AsSpan();
        var speaker = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var recipient = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        return new(message with { Payload = bytes.ToArray() }, speaker, recipient,
            speaker == 0 ? null : ReadString(bytes[NameOffset..]), ReadString(bytes[TextOffset..]));
    }

    private static string ReadString(ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset + 1 < bytes.Length; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]) != 0) continue;
            // Native wchar_t operations preserve code units, including unpaired surrogates.
            var chars = new char[offset / 2];
            for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
            return new string(chars);
        }
        throw new InvalidDataException("Gold chat has an unterminated UTF-16 string.");
    }
}
