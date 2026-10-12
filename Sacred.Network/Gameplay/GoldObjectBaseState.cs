using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Verified Gold cObject fields. Unknown names identify native memory offsets, not stream offsets.</summary>
public readonly record struct GoldObjectBaseState(uint ObjectReference, uint ObjectType, uint Flags,
    ushort Sector, int X, int Y, byte Surface, uint Word28, uint Word34, byte Byte38,
    byte Owner, uint Word3C)
{
    /// <summary>Read only the base block; callers retain all following subtype bytes.</summary>
    public static GoldObjectBaseState FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < GoldObjectStateLayout.Size)
            throw new InvalidDataException("Gold base object state requires at least 41 bytes.");
        if (Word(payload, GoldObjectStateLayout.MarkerOffset) != GoldObjectStateLayout.Marker)
            throw new InvalidDataException("Unsupported Gold base object state marker.");
        return new(Word(payload, GoldObjectStateLayout.ReferenceOffset), Word(payload, GoldObjectStateLayout.TypeOffset),
            Word(payload, GoldObjectStateLayout.FlagsOffset),
            BinaryPrimitives.ReadUInt16LittleEndian(payload[GoldObjectStateLayout.SectorOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[GoldObjectStateLayout.XOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[GoldObjectStateLayout.YOffset..]),
            payload[GoldObjectStateLayout.SurfaceOffset], Word(payload, GoldObjectStateLayout.Word28Offset),
            Word(payload, GoldObjectStateLayout.Word34Offset), payload[GoldObjectStateLayout.Byte38Offset],
            payload[GoldObjectStateLayout.OwnerOffset], Word(payload, GoldObjectStateLayout.Word3COffset));
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
