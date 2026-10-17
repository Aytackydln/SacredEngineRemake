using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Immutable received/persisted regional CreateNPC substitutions. This resolves
/// inputs only; it does not reserve references, construct actors or publish world changes.</summary>
public sealed class SacredScriptRegionReplacementTable
{
    private readonly byte[] _data;
    private SacredScriptRegionReplacementTable(byte[] data) => _data = data;

    public static SacredScriptRegionReplacementTable Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != SacredScriptRegionReplacementLayout.TableSize)
            throw new InvalidDataException("Regional NPC replacement table requires 50 records of 172 bytes.");
        return new(data.ToArray());
    }

    /// <summary>Gold 0x482F18 requires command mask 0x3C clear and operand 0x6B's
    /// runtime selector equal to zero. Callers must establish those gates first.</summary>
    public SacredScriptCreatureReplacement Resolve(byte rawRegion, uint typeId, uint behavior)
    {
        if (rawRegion >= SacredScriptRegionReplacementLayout.RegionCount)
            throw new InvalidDataException($"Raw region {rawRegion} exceeds the native NPC replacement table.");
        var record = _data.AsSpan(rawRegion * SacredScriptRegionReplacementLayout.SerializedSize,
            SacredScriptRegionReplacementLayout.SerializedSize);
        var count = BinaryPrimitives.ReadInt32LittleEndian(record[SacredScriptRegionReplacementLayout.ReplacementCountOffset..]);
        if (count > SacredScriptRegionReplacementLayout.EntryCapacity)
            throw new InvalidDataException("Regional NPC replacement count exceeds its record storage.");
        for (var index = 0; index < count; index++)
        {
            var entry = record.Slice(SacredScriptRegionReplacementLayout.EntriesOffset + index * SacredScriptRegionReplacementEntryLayout.SerializedSize,
                SacredScriptRegionReplacementEntryLayout.SerializedSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != typeId) continue;
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var replacementBehavior = (packed & 0xFFFF0000) == 0 ? behavior
                : (behavior & 0xFFFF0000) | unchecked((uint)((int)packed >> 16));
            return new(unchecked((ushort)packed), replacementBehavior, true);
        }
        return new(typeId, behavior, false);
    }
}
