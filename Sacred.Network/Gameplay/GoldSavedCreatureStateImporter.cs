using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Assembles complete creature packets with owned equipment/mount references and retained saved values.</summary>
public static class GoldSavedCreatureStateImporter
{
    public static GoldCreatureState Import(SacredSavedCreature source, GoldPhysicalState physical,
        IReadOnlyDictionary<uint, uint> references, ushort opcode, byte mode = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(references);
        if (source.Count9 > ushort.MaxValue) throw new InvalidDataException("Creature record count exceeds the network ushort count.");
        var values = GoldSavedCreatureValuesImporter.Import(source.Values, opcode).ToPayload();
        var payload = new byte[GoldCreatureStateLayout.PrefixSize + values.Length + 13 + source.Count9 * 9];
        physical.ToPayload().CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldCreatureStateLayout.MarkerOffset), GoldCreatureStateLayout.Marker);
        for (var i = 0; i < SacredSavedCreatureLayout.EquipmentCount; i++)
        {
            var saved = source.GetEquipmentReference(i);
            var target = saved == 0 ? 0 : references.TryGetValue(saved, out var owned) ? owned
                : throw new InvalidDataException("Saved creature equipment has no participant reference.");
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldCreatureStateLayout.EquipmentOffset + i * 4), target);
        }
        var file = source.ToPayload();
        CopyField(file, payload, 0x1F0, GoldCreatureStateLayout.Word1F0Offset, 20);
        CopyField(file, payload, 0x245, GoldCreatureStateLayout.Word245Offset, 24);
        CopyField(file, payload, 0x25F, GoldCreatureStateLayout.Word25FOffset, 4);
        CopyField(file, payload, 0x2B6, GoldCreatureStateLayout.Byte2B6Offset, 23);
        file.AsSpan(SacredSavedCreatureLayout.Block2EDOffset, GoldCreatureStateLayout.Block2EDSize)
            .CopyTo(payload.AsSpan(GoldCreatureStateLayout.Block2EDOffset));
        var position = GoldCreatureStateLayout.PrefixSize;
        values.CopyTo(payload, position);
        position += values.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(position), checked((ushort)source.Count9));
        position += 2;
        for (var i = 0; i < source.Count9; i++, position += 9) source.GetRecord9(i).CopyTo(payload.AsSpan(position));
        // Mode zero is the ordinary-session default; special native hero modes are supplied by the caller.
        payload[position++] = mode;
        var linked = source.MountPartnerReference;
        var linkedTarget = linked == 0 ? 0 : references.TryGetValue(linked, out var ownedPartner) ? ownedPartner
            : throw new InvalidDataException("Saved creature mount partner has no participant reference.");
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(position), linkedTarget);
        file.AsSpan(SacredSavedCreatureLayout.Word574Offset, 2).CopyTo(payload.AsSpan(position + 4));
        file.AsSpan(SacredSavedCreatureLayout.Word578Offset, 4).CopyTo(payload.AsSpan(position + 6));
        // TODO: Apply gameplay initialization/derived stats here when actor simulation is bound.
        // The saved 68-byte list and optional hero attachment belong to the file format, not this packet.
        return GoldCreatureState.FromPayload(payload);
    }

    private static void CopyField(ReadOnlySpan<byte> file, Span<byte> target, int nativeOffset, int offset, int size)
        => file.Slice(SacredSavedCreatureLayout.Block1F0Offset + nativeOffset - 0x1F0, size).CopyTo(target[offset..]);
}
