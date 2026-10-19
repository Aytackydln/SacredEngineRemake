using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x585C60 value writer, shared by newly constructed and retained actors.</summary>
public static class GoldCreatureValuesWriter
{
    public static GoldCreatureValuesState Write(ushort opcode, ReadOnlySpan<byte> block08, ReadOnlySpan<byte> block128,
        ReadOnlySpan<byte> records22 = default, ReadOnlySpan<byte> records38 = default, uint flags148 = 0)
    {
        if (block08.Length != GoldCreatureValuesLayout.FullBlock08Size || block128.Length != GoldCreatureValuesLayout.Block128Size)
            throw new ArgumentException("Creature value blocks require the original 238/24-byte extents.");
        if (records22.Length % GoldCreatureValuesLayout.Record22Size != 0 || records38.Length % GoldCreatureValuesLayout.Record38Size != 0)
            throw new ArgumentException("Creature value lists contain an incomplete record.");
        var count22 = checked((ushort)(records22.Length / GoldCreatureValuesLayout.Record22Size));
        var count38 = checked((ushort)(records38.Length / GoldCreatureValuesLayout.Record38Size));
        var full = GoldCreatureValuesLayout.IsFull(opcode);
        var payload = new byte[(full ? GoldCreatureValuesLayout.FullMinimumSize : GoldCreatureValuesLayout.CompactMinimumSize)
            + records22.Length + records38.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, GoldCreatureValuesLayout.Marker);
        block128.CopyTo(payload.AsSpan(GoldCreatureValuesLayout.Block128Offset));
        if (full) block08.CopyTo(payload.AsSpan(GoldCreatureValuesLayout.FullBlock08Offset));
        else
        {
            Copy(block08, payload, 0x0C, GoldCreatureValuesLayout.CompactWord0COffset, 4);
            Copy(block08, payload, 0x24, GoldCreatureValuesLayout.CompactBlock24Offset, 8);
            Copy(block08, payload, 0x2C, GoldCreatureValuesLayout.CompactBlock2COffset, 8);
            Copy(block08, payload, 0x46, GoldCreatureValuesLayout.CompactWord46Offset, 4);
            Copy(block08, payload, 0x56, GoldCreatureValuesLayout.CompactWord56Offset, 2);
        }
        var cursor = full ? GoldCreatureValuesLayout.FullCount22Offset : GoldCreatureValuesLayout.CompactCount22Offset;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(cursor), count22);
        records22.CopyTo(payload.AsSpan(cursor += 2));
        cursor += records22.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(cursor), count38);
        records38.CopyTo(payload.AsSpan(cursor += 2));
        cursor += records38.Length;
        if (!full)
        {
            Copy(block08, payload, 0x42, cursor, 4);
            Copy(block08, payload, 0x1C, cursor + 4, 8);
            Copy(block08, payload, 0x58, cursor + 12, 1);
            cursor += 13;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), flags148);
        return GoldCreatureValuesState.FromPayload(payload, opcode);
    }

    private static void Copy(ReadOnlySpan<byte> block08, Span<byte> target, int native, int offset, int length) =>
        block08.Slice(native - 8, length).CopyTo(target[offset..]);
}
