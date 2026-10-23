using System.Buffers.Binary;
using System.Text;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldMovementCallbackEvent(GoldMessage Message, uint FunctionIndex, string Name) : GoldScriptWorldEvent(Message);

/// <summary>OnMoveOver registration: function word and strncpy-normalized 128-byte name.</summary>
public static class GoldMovementCallbackCodec
{
    public const ushort Opcode = 0x1D1;
    public const int NameSize = 128;
    public static GoldMovementCallbackEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < 5)
            throw new InvalidDataException("Gold movement callback requires a function word and name bytes.");
        var bytes = message.Payload.AsSpan(4);
        var end = bytes[..Math.Min(bytes.Length, NameSize-1)].IndexOf((byte)0);
        if (end < 0)
        {
            if (bytes.Length < NameSize-1) throw new InvalidDataException("Truncated Gold movement callback name.");
            end = NameSize-1;
        }
        return new(message with { Payload = message.Payload.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload),
            Encoding.Latin1.GetString(bytes[..end]));
    }
}
