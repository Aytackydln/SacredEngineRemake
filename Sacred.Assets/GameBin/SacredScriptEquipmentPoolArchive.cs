using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Immutable original wea.bin pool inputs. Loading preserves order,
/// duplicates, full UInt32 IDs and the otherwise inaccessible script pool 255.</summary>
public sealed class SacredScriptEquipmentPoolArchive
{
    private readonly IReadOnlyList<uint>[] _pools;
    private readonly byte[] _source;
    private SacredScriptEquipmentPoolArchive(IReadOnlyList<uint>[] pools, byte[] source, int consumed)
    { _pools = pools; _source = source; BytesConsumed = consumed; }
    public int BytesConsumed { get; }
    public int EntryCount => _pools.Sum(pool => pool.Count);
    public ReadOnlyMemory<byte> RemainingData => _source.AsMemory(BytesConsumed);
    public IReadOnlyList<uint> Get(int index) => (uint)index < _pools.Length ? _pools[index] :
        throw new ArgumentOutOfRangeException(nameof(index));
    public byte[] ToPayload() => _source.ToArray();
    public static SacredScriptEquipmentPoolArchive Load(string path) => FromPayload(File.ReadAllBytes(path));
    public static SacredScriptEquipmentPoolArchive FromPayload(ReadOnlySpan<byte> bytes)
    {
        var source = bytes.ToArray();
        var pools = new IReadOnlyList<uint>[SacredScriptEquipmentPoolLayout.FilePoolCount];
        var position = 0;
        for (var i = 0; i < pools.Length; i++)
        {
            if (source.Length - position < SacredScriptEquipmentPoolLayout.SerializedSize)
                throw new InvalidDataException($"Truncated wea.bin pool {i} count.");
            var count = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(position));
            position += SacredScriptEquipmentPoolLayout.SerializedSize;
            if (count > (source.Length - position) / SacredScriptEquipmentPoolLayout.TypeIdSize)
                throw new InvalidDataException($"Truncated wea.bin pool {i} resource IDs.");
            var entries = new uint[(int)count];
            for (var j = 0; j < entries.Length; j++, position += SacredScriptEquipmentPoolLayout.TypeIdSize)
                entries[j] = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(position));
            pools[i] = Array.AsReadOnly(entries);
        }
        Console.WriteLine($"Script equipment pools loaded: {pools.Length} pools, {pools.Sum(pool => pool.Count)} entries.");
        return new(pools, source, position);
    }
}
