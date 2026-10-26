using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

/// <summary>Original counted bonus pools and second pair map (0xAD57BC).
/// Gold 0x5CF4B0 copies the latter into its rarity-distribution vector.</summary>
public static class EquipmentMagicBalanceBinReader
{
    public static SacredEquipmentMagicBalance Read(ReadOnlySpan<byte> file)
    {
        var offset = 0;
        Take(file, ref offset, Count(file, ref offset));
        var pools = new Dictionary<uint, IReadOnlyList<SacredEquipmentMagicPoolLayout>>();
        var count = Count(file, ref offset);
        for (var i = 0; i < count; i++)
        {
            var key = Word(file, ref offset);
            var rows = Count(file, ref offset);
            if (rows > (file.Length - offset) / SacredEquipmentMagicPoolLayout.SerializedSize) throw new InvalidDataException("Truncated magic bonus pool.");
            pools[key] = Array.AsReadOnly(MemoryMarshal.Cast<byte, SacredEquipmentMagicPoolLayout>(
                Take(file, ref offset, rows * SacredEquipmentMagicPoolLayout.SerializedSize)).ToArray());
        }
        var weights = new Dictionary<uint, uint>();
        count = Count(file, ref offset);
        for (var i = 0; i < count; i++)
        {
            var row = MemoryMarshal.Read<SacredEquipmentRarityWeightLayout>(Take(file, ref offset, SacredEquipmentRarityWeightLayout.SerializedSize));
            weights[row.Selector] = row.Weight;
        }
        return new(pools, weights);
    }
    private static uint Word(ReadOnlySpan<byte> bytes, ref int at) => BinaryPrimitives.ReadUInt32LittleEndian(Take(bytes, ref at, 4));
    private static int Count(ReadOnlySpan<byte> bytes, ref int at)
    {
        var value = Word(bytes, ref at);
        return value <= int.MaxValue ? (int)value : throw new InvalidDataException("Invalid magic table count.");
    }
    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> bytes, ref int at, int length)
    {
        if (length < 0 || length > bytes.Length - at) throw new InvalidDataException("Truncated magic tuning.");
        var result = bytes.Slice(at, length); at += length; return result;
    }
}
