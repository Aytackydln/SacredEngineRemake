using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Projects retained file values into the opcode-specific network layout.</summary>
public static class GoldSavedCreatureValuesImporter
{
    public static GoldCreatureValuesState Import(SacredSavedCreatureValues source, ushort opcode, uint runtimeWord148 = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        var full = GoldCreatureValuesLayout.IsFull(opcode);
        var payload = new byte[(full ? GoldCreatureValuesLayout.FullMinimumSize : GoldCreatureValuesLayout.CompactMinimumSize)
            + source.Count22 * 22 + source.Count38 * 38];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, GoldCreatureValuesLayout.Marker);
        source.Block128.CopyTo(payload.AsSpan(4));
        if (full) source.Block08.CopyTo(payload.AsSpan(28));
        else
        {
            CopyField(source, payload, 0x0C, 28, 4);
            CopyField(source, payload, 0x24, 32, 8);
            CopyField(source, payload, 0x2C, 40, 8);
            CopyField(source, payload, 0x46, 48, 4);
            CopyField(source, payload, 0x56, 52, 2);
        }
        var position = full ? GoldCreatureValuesLayout.FullCount22Offset : GoldCreatureValuesLayout.CompactCount22Offset;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(position), checked((ushort)source.Count22));
        position += 2;
        for (var i = 0; i < source.Count22; i++, position += 22) source.GetRecord22(i).CopyTo(payload.AsSpan(position));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(position), checked((ushort)source.Count38));
        position += 2;
        for (var i = 0; i < source.Count38; i++, position += 38) source.GetRecord38(i).CopyTo(payload.AsSpan(position));
        if (!full)
        {
            CopyField(source, payload, 0x42, position, 4);
            CopyField(source, payload, 0x1C, position + 4, 8);
            CopyField(source, payload, 0x58, position + 12, 1);
            position += 13;
        }
        // +0x148 is runtime-only, absent from the saved value blocks. A new session supplies zero.
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(position), runtimeWord148);
        // TODO: Recalculate derived creature values when gameplay needs them. Retain saved values for entry;
        // the file-only 28-byte list is deliberately absent from this network format.
        return GoldCreatureValuesState.FromPayload(payload, opcode);
    }

    private static void CopyField(SacredSavedCreatureValues source, Span<byte> target, int nativeOffset, int offset, int size)
        => source.Block08.Slice(nativeOffset - 8, size).CopyTo(target[offset..]);
}
