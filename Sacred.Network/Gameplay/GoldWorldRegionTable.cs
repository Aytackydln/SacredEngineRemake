using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.World.Regions;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable native region records. Preserves padding and all unmodified fields.</summary>
public sealed class GoldWorldRegionTable
{
    private readonly byte[] _data;
    private GoldWorldRegionTable(byte[] data) => _data = data;

    /// <summary>Actual Gold constructor defaults, independent of captured banks.</summary>
    public static GoldWorldRegionTable CreateInitial()
    {
        var data = new byte[GoldWorldRegionTableLayout.Size];
        for (var i = 0; i < GoldWorldRegionTableLayout.Count; i++)
        {
            var row = data.AsSpan(i * GoldWorldRegionTableLayout.RecordSize, GoldWorldRegionTableLayout.RecordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(row, (ushort)i);
            foreach (var offset in new[] { 12, 16, 20 })
                BinaryPrimitives.WriteUInt32LittleEndian(row[offset..], 0x3F800000);
        }
        return new(data);
    }

    public static GoldWorldRegionTable FromPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length != GoldWorldRegionTableLayout.Size)
            throw new InvalidDataException("Gold region table requires exactly 256 records of 24 bytes.");
        return new(data.ToArray());
    }

    public ReadOnlySpan<byte> GetRecord(int index)
    {
        if ((uint)index >= GoldWorldRegionTableLayout.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _data.AsSpan(index * GoldWorldRegionTableLayout.RecordSize, GoldWorldRegionTableLayout.RecordSize);
    }
    public uint GetFlags(int index) => BinaryPrimitives.ReadUInt32LittleEndian(GetRecord(index)[GoldWorldRegionTableLayout.FlagsOffset..]);
    public SacredRegionStateLayout GetState(int index) => MemoryMarshal.Read<SacredRegionStateLayout>(GetRecord(index));
    public bool IsInitialized(int index) => (GetFlags(index) & GoldWorldRegionTableLayout.InitializedFlag) != 0;
    public byte[] ToPayload() => _data.ToArray();

    /// <summary>Native setters select the low byte even for a noncanonical received region word.</summary>
    public GoldWorldRegionTable WithFlags(uint region, uint mask, bool set)
    {
        var data = ToPayload();
        var field = data.AsSpan(unchecked((byte)region) * GoldWorldRegionTableLayout.RecordSize + GoldWorldRegionTableLayout.FlagsOffset);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(field);
        BinaryPrimitives.WriteUInt32LittleEndian(field, set ? flags | mask : flags & ~mask);
        return new(data);
    }
}
