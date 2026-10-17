using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless nine-slot sector-entry instructions; no world/script side effects or actor binding.</summary>
public sealed class GoldSectorEntry
{
    private readonly byte[] _payload;
    private GoldSectorEntry(byte[] payload) => _payload = payload;
    public int Count => GoldSectorEntryLayout.SlotCount;
    public uint Word0 => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(GoldSectorEntryLayout.Word0Offset));
    public uint Word58 => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(GoldSectorEntryLayout.Word58Offset));
    public GoldSectorEntrySlot GetSlot(int index)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return new(BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(GoldSectorEntryLayout.SectorsOffset + index * 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(GoldSectorEntryLayout.RetainedWordsOffset + index * 2)),
            _payload[GoldSectorEntryLayout.ScriptModesOffset + index], _payload[GoldSectorEntryLayout.InitializeFlagsOffset + index]);
    }

    public static GoldSectorEntry FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldSectorEntryLayout.Size)
            throw new InvalidDataException("Gold sector entry requires exactly 62 bytes.");
        return new(payload.ToArray());
    }

    public byte[] ToPayload() => _payload.ToArray();
}
