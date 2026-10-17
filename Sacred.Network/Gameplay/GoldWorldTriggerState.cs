using System.Buffers.Binary;
using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>Gold bank 1 section 6: original cTrigger stream, retaining unchecked marker bits on receipt.</summary>
public readonly record struct GoldWorldTriggerState(uint Reference, ushort Flags, ushort State, uint Referent, uint Parameter,
    uint Marker = SacredWorldTriggerLayout.FormatMarker)
{
    public static GoldWorldTriggerState FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredWorldTriggerLayout.SerializedSize) throw new InvalidDataException("World trigger requires 20 bytes.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    }
    public byte[] ToPayload()
    {
        var bytes = new byte[SacredWorldTriggerLayout.SerializedSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), Reference);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), State);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), Referent);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), Parameter);
        return bytes;
    }
}
