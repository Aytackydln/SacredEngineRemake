using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x8060E0 case 0x179: wake, then relocate or retain the native deferred-position slot.</summary>
public static class GoldCreaturePositionCorrectionCodec
{
    public const ushort Opcode = 0x179;
    public const uint DeferredPositionFlag = 0x20000;
    public static GoldObjectPositionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        if (message.Payload.Length < GoldObjectPositionCodec.PayloadSize)
            throw new InvalidDataException("Gold creature position correction requires at least 24 bytes.");
        var copy = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload.AsSpan(offset));
        return new(copy, Word(0), new(unchecked((int)Word(4)), unchecked((int)Word(8)), Word(12), Word(16)), Word(20));
    }
    public static GoldMessage Create(byte slot, uint reference, GoldObjectPosition position, uint facingBits) =>
        GoldObjectPositionCodec.Create(slot, reference, position, facingBits) with { Opcode = Opcode };
}
