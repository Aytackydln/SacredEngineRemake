using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x165, consumed by 0x4969D0/0x46D760: ref, X/Y, surface, sector, facing bits.</summary>
public static class GoldObjectPositionCodec
{
    public const ushort Opcode = 0x165;
    public const int PayloadSize = 24;

    public static GoldObjectPositionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        var payload = message.Payload.AsSpan();
        if (payload.Length != PayloadSize) throw new InvalidDataException("Gold 0x165 requires exactly 24 bytes.");
        return new(message, Word(payload, 0), new(unchecked((int)Word(payload, 4)), unchecked((int)Word(payload, 8)),
            Word(payload, 12), Word(payload, 16)), Word(payload, 20));
    }

    public static GoldMessage Create(byte playerSlot, uint objectReference, GoldObjectPosition position, uint facingBits)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[PayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, objectReference);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), position.X);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), position.Y);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), position.Surface);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), position.Sector);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), facingBits);
        return new(Opcode, playerSlot, playerSlot, 0, payload);
    }

    private static uint Word(ReadOnlySpan<byte> payload, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
}
