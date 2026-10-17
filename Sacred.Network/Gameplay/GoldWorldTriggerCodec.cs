using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldWorldTriggerEvent(GoldMessage Message, uint Reference, uint Value, uint InitialState = 0,
    string? Name = null) : GoldGameEvent(Message);

/// <summary>Gold client 0x7DF6BF/0x7DF6FF/0x7DF756. References select trigger slots, not objects.</summary>
public static class GoldWorldTriggerCodec
{
    public const ushort CreateOpcode = 0x1AE;
    public const ushort ClearOpcode = 0x1AF;
    public const ushort SetOpcode = 0x1B0;

    public static GoldWorldTriggerEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Payload.AsSpan();
        if (message.Opcode == CreateOpcode)
        {
            if (bytes.Length < 13) throw new InvalidDataException("Missing world trigger creation fields/name.");
            var end = bytes[12..].IndexOf((byte)0);
            if (end < 0 || end >= GoldScriptResourceLayout.NameSize)
                throw new InvalidDataException("Trigger name has no bounded terminator.");
            return new(message with { Payload = bytes.ToArray() }, Word(bytes, 0), Word(bytes, 4), Word(bytes, 8),
                GoldScriptNames.Read(bytes.Slice(12, end)));
        }
        if (message.Opcode is not (SetOpcode or ClearOpcode)) throw new ArgumentOutOfRangeException(nameof(message));
        if (bytes.Length < 8) throw new InvalidDataException("World trigger state message requires two words.");
        return new(message with { Payload = bytes.ToArray() }, Word(bytes, 0), Word(bytes, 4));
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
