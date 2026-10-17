using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>A view into an immutable snapshot. Prefix and element bytes keep their native representation.</summary>
public sealed class GoldWorldBankRecord
{
    private readonly ReadOnlyMemory<byte> _prefix;
    private readonly ReadOnlyMemory<byte> _data;
    internal GoldWorldBankRecord(ReadOnlyMemory<byte> prefix, ReadOnlyMemory<byte> data, int elementSize)
        : this(prefix, data, elementSize, [], []) { }

    internal GoldWorldBankRecord(ReadOnlyMemory<byte> prefix, ReadOnlyMemory<byte> data, int elementSize,
        GoldWorldBankRecord[] vectors, GoldWorldBankRecord[] childRecords)
    {
        _prefix = prefix;
        _data = data;
        ElementSize = elementSize;
        Vectors = Array.AsReadOnly(vectors);
        ChildRecords = Array.AsReadOnly(childRecords);
    }

    /// <summary>Name, key/sector ID, composite fixed prefix, or empty for fixed records.</summary>
    public ReadOnlySpan<byte> Prefix => _prefix.Span;
    public ReadOnlySpan<byte> Data => _data.Span;
    /// <summary>Composite Data retains encoded counts and nested body bytes with ElementSize=1.</summary>
    public int ElementSize { get; }
    public int Count => _data.Length / ElementSize;
    public IReadOnlyList<GoldWorldBankRecord> Vectors { get; }
    /// <summary>Bank 4 section 1: each child's Data contains its 24 fixed bytes; Vectors[0] holds its preceding word vector.</summary>
    public IReadOnlyList<GoldWorldBankRecord> ChildRecords { get; }
    public ReadOnlySpan<byte> GetElement(int index)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Data.Slice(index * ElementSize, ElementSize);
    }

    /// <summary>Only valid for the keyed-vector or sector-record section.</summary>
    public uint Key => Prefix.Length == sizeof(uint) ? BinaryPrimitives.ReadUInt32LittleEndian(Prefix) :
        throw new InvalidOperationException("This record does not have a four-byte key.");
}
