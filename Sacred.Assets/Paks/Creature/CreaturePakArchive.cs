using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;

namespace Sacred.Assets.Paks.Creature;

/// <summary>Reads the selected installation's CIF table and resolves native item-type identity.</summary>
public sealed class CreaturePakArchive
{
    private CreaturePakArchive(IReadOnlyList<SacredCreatureTemplate> entries)
    {
        Entries = entries;
        ByItemType = entries.Where(entry => entry.RecordIndex != 0).ToFrozenDictionary(entry => entry.ItemType);
    }

    public IReadOnlyList<SacredCreatureTemplate> Entries { get; }
    public FrozenDictionary<uint, SacredCreatureTemplate> ByItemType { get; }
    public static CreaturePakArchive Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Recreates the native loader's ItemType-to-record association before resolving
    /// an authored Items.pak +0x1A reference. Installed item descriptors can contain zero here;
    /// Demo 0x4214C4 and Gold 0x4372C9 synthesize the runtime index from Creature.pak.</summary>
    public SacredCreatureTemplate? ResolveTemplate(ItemsPakEntry item)
    {
        if (ByItemType.TryGetValue(item.ItemIndex, out var mapped)) return mapped;
        var index = item.ModelDesc.CreatureEntry;
        if (index == 0) return null;
        if (index >= Entries.Count)
            throw new InvalidDataException($"Items.pak creature index {index} exceeds Creature.pak.");
        return Entries[index];
    }

    public static CreaturePakArchive Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < CreaturePakHeaderLayout.Size)
            throw new InvalidDataException("Creature.pak header is truncated.");
        var header = MemoryMarshal.Read<CreaturePakHeaderLayout>(bytes);
        if (header.Signature != 0x00464943 || header.Count == 0)
            throw new InvalidDataException("Creature.pak has an invalid CIF header.");
        var payloadSize = bytes.Length - CreaturePakHeaderLayout.Size;
        if (payloadSize % header.Count != 0)
            throw new InvalidDataException("Creature.pak count does not match its record data.");
        var stride = checked((int)(payloadSize / header.Count));
        if (stride is not (CreatureDemoRecordLayout.Size or CreatureGoldRecordLayout.Size))
            throw new InvalidDataException($"Unsupported Creature.pak record size: {stride}.");
        var result = new SacredCreatureTemplate[header.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = CreaturePakHeaderLayout.Size + index * stride;
            var record = bytes.Slice(offset, stride);
            var gold = stride == CreatureGoldRecordLayout.Size;
            var speedOffset = gold ? 38 : 36;
            var bonusOffset = speedOffset + 4;
            var bonuses = new SacredCreatureInnateBonus[6];
            for (var bonus = 0; bonus < bonuses.Length; bonus++)
                bonuses[bonus] = new(U16(record, bonusOffset + bonus * 2),
                    record[bonusOffset + 12 + bonus], record[bonusOffset + 18 + bonus]);
            result[index] = new(index, offset, stride, BinaryPrimitives.ReadUInt32LittleEndian(record),
                (SacredCreatureClass)record[4], record[5], U16(record, 6), U16(record, 8), U16(record, 10),
                record[12], record[15], record[13], record[14], record[17], record[16], record[19],
                U16(record, speedOffset), U16(record, speedOffset + 2),
                Array.AsReadOnly(record.Slice(20, 2).ToArray()),
                Array.AsReadOnly(record.Slice(22, gold ? 16 : 14).ToArray()),
                Array.AsReadOnly(bonuses), Array.AsReadOnly(gold ? record[66..86].ToArray() : Array.Empty<byte>()));
        }
        return new(Array.AsReadOnly(result));
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
}
