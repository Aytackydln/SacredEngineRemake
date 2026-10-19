using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Serialized new non-hero creature defaults from Gold 0x52DE40.
/// The caller performs resource resolution, allocation, values and placement.</summary>
public static class GoldCreatureConstructionWriter
{
    public static GoldCreatureState Write(GoldPhysicalState physical, GoldCreatureValuesState values,
        uint creatureClass, uint behavior, ushort equipmentSelector, ushort randomTail)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(values);
        if (!physical.RemainingData.IsEmpty) throw new ArgumentException("Construction requires exactly one physical prefix.", nameof(physical));
        if (values.IsFull) throw new ArgumentException("New-creature publication requires compact values.", nameof(values));
        if (randomTail >= 6) throw new ArgumentOutOfRangeException(nameof(randomTail));
        var valueBytes = values.ToPayload();
        var bytes = new byte[GoldCreatureStateLayout.PrefixSize + valueBytes.Length + 13];
        physical.ToPayload().CopyTo(bytes, 0);
        Word(GoldCreatureStateLayout.MarkerOffset, GoldCreatureStateLayout.Marker);
        Word(GoldCreatureStateLayout.Word1F0Offset, creatureClass);
        Word(GoldCreatureStateLayout.Word1F4Offset, behavior);
        Word(GoldCreatureStateLayout.Word24DOffset, uint.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word25FOffset), equipmentSelector);
        Word(GoldCreatureStateLayout.Word2C9Offset, BitConverter.SingleToUInt32Bits(1));
        valueBytes.CopyTo(bytes, GoldCreatureStateLayout.PrefixSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 6), randomTail);
        return GoldCreatureState.FromPayload(bytes);
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    }
}
