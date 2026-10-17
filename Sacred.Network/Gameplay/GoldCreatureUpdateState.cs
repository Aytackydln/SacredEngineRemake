using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable 0x11A state; does not include equipment, physical state, action bank or values.</summary>
public sealed class GoldCreatureUpdateState
{
    private readonly byte[] _payload;
    private GoldCreatureUpdateState(byte[] payload) => _payload = payload;

    public uint Word1F0 => Word(GoldCreatureUpdateLayout.Word1F0Offset);
    public uint Word1F4 => Word(GoldCreatureUpdateLayout.Word1F4Offset);
    public uint Word1F8 => Word(GoldCreatureUpdateLayout.Word1F8Offset);
    public byte Byte1FC => _payload[GoldCreatureUpdateLayout.Byte1FCOffset];
    public byte Byte1FD => _payload[GoldCreatureUpdateLayout.Byte1FDOffset];
    public ushort Word1FE => Short(GoldCreatureUpdateLayout.Word1FEOffset);
    public uint Word200 => Word(GoldCreatureUpdateLayout.Word200Offset);
    public uint Word245 => Word(GoldCreatureUpdateLayout.Word245Offset);
    public uint Word249 => Word(GoldCreatureUpdateLayout.Word249Offset);
    public uint Word24D => Word(GoldCreatureUpdateLayout.Word24DOffset);
    public uint Word251 => Word(GoldCreatureUpdateLayout.Word251Offset);
    public uint Word255 => Word(GoldCreatureUpdateLayout.Word255Offset);
    public uint Word259 => Word(GoldCreatureUpdateLayout.Word259Offset);
    public ushort Word25F => Short(GoldCreatureUpdateLayout.Word25FOffset);
    public ushort Word261 => Short(GoldCreatureUpdateLayout.Word261Offset);
    public byte Byte2B6 => _payload[GoldCreatureUpdateLayout.Byte2B6Offset];
    public ushort Word2B7 => Short(GoldCreatureUpdateLayout.Word2B7Offset);
    public ReadOnlySpan<byte> Block2B9 => _payload.AsSpan(GoldCreatureUpdateLayout.Block2B9Offset, GoldCreatureStateLayout.Block2B9Size);
    public uint Word2C9 => Word(GoldCreatureUpdateLayout.Word2C9Offset);

    public static GoldCreatureUpdateState FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldCreatureUpdateLayout.StateSize ||
            !GoldObjectStateReader.HasMarker(payload, 0, GoldCreatureStateLayout.Marker))
            throw new InvalidDataException("Gold 0x11A requires a 75-byte creature state with its marker.");
        return new(payload.ToArray());
    }

    public byte[] ToPayload() => _payload.ToArray();

    /// <summary>Apply the original 0x54C93F field copies and masks. Derived-value recalculation is deferred.</summary>
    public GoldCreatureState ApplyTo(GoldCreatureState previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var bytes = previous.ToPayload();
        _payload.AsSpan(GoldCreatureUpdateLayout.MarkerSize).CopyTo(bytes.AsSpan(GoldCreatureStateLayout.Word1F0Offset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word1F4Offset),
            Merge(previous.Word1F4, Word1F4, GoldCreatureUpdateLayout.Preserve1F4Mask));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word200Offset),
            Merge(previous.Word200, Word200, GoldCreatureUpdateLayout.Preserve200Mask));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word2B7Offset),
            (ushort)Merge(previous.Word2B7, Word2B7, GoldCreatureUpdateLayout.Preserve2B7Mask));
        return GoldCreatureState.FromPayload(bytes);
    }

    private static uint Merge(uint previous, uint incoming, uint mask) => (previous & mask) | (incoming & ~mask);
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
    private ushort Short(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(offset));
}
