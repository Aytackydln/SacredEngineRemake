using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x7D8950 selector 6 copies a serialized object after an eight- or 24-byte prefix.</summary>
public static class GoldObjectStateCodec
{
    public static GoldMessage Create(ushort opcode, byte playerSlot, ReadOnlySpan<byte> objectState)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var prefixSize = opcode switch
        {
            0x169 or 0x16B or 0x16F => 24,
            0x16C or 0x16E => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(opcode), "Unsupported Gold object state opcode.")
        };
        GoldObjectStateReader.Validate(objectState, opcode);
        var state = GoldObjectBaseState.FromPayload(objectState);
        var payload = new byte[prefixSize + objectState.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, state.ObjectReference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), state.ObjectType);
        if (prefixSize == 24)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), state.X);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), state.Y);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), state.Surface);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), state.Sector);
        }
        objectState.CopyTo(payload.AsSpan(prefixSize));
        return new(opcode, playerSlot, playerSlot, 0, payload);
    }
}
