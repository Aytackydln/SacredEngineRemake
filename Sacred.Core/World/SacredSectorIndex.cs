using System.Collections.Frozen;
using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Authored sector identities and neighbors from the original sectors.keyx records.</summary>
public sealed class SacredSectorIndex
{
    private readonly FrozenDictionary<uint, KeyxSectorRecord> _records;
    private SacredSectorIndex(Dictionary<uint, KeyxSectorRecord> records, uint declaredSectorCount)
    {
        _records = records.ToFrozenDictionary();
        DeclaredSectorCount = declaredSectorCount;
    }
    public int Count => _records.Count;
    public uint DeclaredSectorCount { get; }
    /// <summary>Gold 0x638222 / 0x638280: declared KEYX count plus the reserved zero slot.
    /// Host finish 0x18 sends this table size through 0x634AE0, not the number of unique sector IDs.</summary>
    public uint RuntimeTableSize => DeclaredSectorCount + 1;
    public bool TryGetSector(uint id, out KeyxSectorRecord sector) => _records.TryGetValue(id, out sector);
    public SacredSectorGrid CreateGrid() => new(_records, RuntimeTableSize);

    public static SacredSectorIndex Read(string path) => FromPayload(File.ReadAllBytes(path));

    public static SacredSectorIndex FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < KeyxHeaderLayout.SerializedSize) throw new InvalidDataException("Sector index is missing its header.");
        var count = MemoryMarshal.Read<KeyxHeaderLayout>(payload).SectorCount;
        if (count > (payload.Length - KeyxHeaderLayout.SerializedSize) / KeyxSectorRecord.Size)
            throw new InvalidDataException("Sector index has a truncated declared record array.");
        var records = new Dictionary<uint, KeyxSectorRecord>();
        for (var i = 0; i < count; i++)
        {
            var record = KeyxSectorRecord.FromBytes(payload.Slice(KeyxHeaderLayout.SerializedSize + i * KeyxSectorRecord.Size, KeyxSectorRecord.Size));
            // Like the native ID-indexed table, later records replace the same ID. Zero denotes no sector.
            if (record.Id != 0) records[record.Id] = record;
        }
        return new(records, count);
    }
}
