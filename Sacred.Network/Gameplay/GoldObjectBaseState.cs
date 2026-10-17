using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Verified Gold cObject fields. Unknown names identify native memory offsets, not stream offsets.</summary>
public readonly record struct GoldObjectBaseState(uint ObjectReference, uint ObjectType, uint Flags,
    ushort Sector, int X, int Y, byte Surface, uint Word28, uint Word34, byte Byte38,
    byte Owner, uint Word3C)
{
    /// <summary>Object trigger reference at native +0x34, assigned by Gold 0x174/0x4177D0.</summary>
    public uint TriggerReference => Word34;
    /// <summary>Native instance +0x3C script-resource identity; zero uses Items.pak +0x20.</summary>
    public uint ScriptResourceId => Word3C;
    public float FacingDegrees => BitConverter.UInt32BitsToSingle(Word28);

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

    /// <summary>Serialize the base writer's fields, including its native flag 0x10000 clearing.</summary>
    public byte[] ToPayload()
    {
        var payload = new byte[GoldObjectStateLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, GoldObjectStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), ObjectReference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), ObjectType);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), Flags & ~0x10000u);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16), Sector);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(18), X);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(22), Y);
        payload[26] = Surface;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(27), Word28);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(31), Word34);
        payload[35] = Byte38;
        payload[36] = Owner;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(37), Word3C);
        return payload;
    }
}
