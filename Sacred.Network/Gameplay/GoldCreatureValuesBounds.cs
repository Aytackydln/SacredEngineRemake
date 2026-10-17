using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Resolve both counted lists before allocation or field access.</summary>
internal readonly record struct GoldCreatureValuesBounds(ushort Count22, int Records22,
    ushort Count38, int Records38, int Suffix, int End)
{
    public static GoldCreatureValuesBounds Read(ReadOnlySpan<byte> payload, bool full)
    {
        if (!GoldObjectStateReader.HasMarker(payload, 0, GoldCreatureValuesLayout.Marker))
            throw new InvalidDataException("Unsupported Gold creature values marker.");
        var count22Offset = full ? GoldCreatureValuesLayout.FullCount22Offset : GoldCreatureValuesLayout.CompactCount22Offset;
        Require(payload, count22Offset + 2);
        var count22 = BinaryPrimitives.ReadUInt16LittleEndian(payload[count22Offset..]);
        var records22 = count22Offset + 2;
        var count38Offset = records22 + count22 * GoldCreatureValuesLayout.Record22Size;
        Require(payload, count38Offset + 2);
        var count38 = BinaryPrimitives.ReadUInt16LittleEndian(payload[count38Offset..]);
        var records38 = count38Offset + 2;
        var suffix = records38 + count38 * GoldCreatureValuesLayout.Record38Size;
        var end = suffix + (full ? GoldCreatureValuesLayout.FullSuffixSize : GoldCreatureValuesLayout.CompactSuffixSize);
        Require(payload, end);
        return new(count22, records22, count38, records38, suffix, end);
    }

    private static void Require(ReadOnlySpan<byte> payload, int size)
    {
        if (payload.Length < size) throw new InvalidDataException("Truncated Gold creature values or counted record list.");
    }
}
