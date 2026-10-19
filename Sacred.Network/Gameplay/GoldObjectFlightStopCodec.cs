using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x8060E0: the 24-byte position form 0x146 also clears Flying.</summary>
public static class GoldObjectFlightStopCodec
{
    public const ushort Opcode = 0x146;
    public static GoldObjectPositionEvent Decode(GoldMessage message)
    {
        if (message.Opcode != Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        var bytes = message.Payload.AsSpan();
        if (bytes.Length != GoldObjectPositionCodec.PayloadSize)
            throw new InvalidDataException("Gold flight stop requires exactly 24 bytes.");
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(offset));
        return new(message with { Payload = bytes.ToArray() }, Word(0),
            new(unchecked((int)Word(4)), unchecked((int)Word(8)), Word(12), Word(16)), Word(20));
    }
}
