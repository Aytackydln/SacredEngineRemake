using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x44A300 physical constructor's stored fields before model
/// activation. The unused attachment vector is initialized to zero.</summary>
public static class GoldPhysicalConstructionWriter
{
    public static GoldPhysicalState Write(GoldObjectBaseState basis, GoldPhysicalVector direction)
    {
        var bytes = new byte[GoldPhysicalStateLayout.Size];
        basis.ToPayload().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.MarkerOffset), GoldPhysicalStateLayout.Marker);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Word94Offset), ushort.MaxValue);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Vector70Offset), direction.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Vector70Offset + 4), direction.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Vector70Offset + 8), direction.Z);
        return GoldPhysicalState.FromPayload(bytes);
    }
}
