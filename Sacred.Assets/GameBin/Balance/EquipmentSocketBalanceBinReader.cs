using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

/// <summary>Reads the prefix through the third counted map. The second pair map
/// is distinct and is not the generation-quality lookup (Gold 0x7120CD).</summary>
public static class EquipmentSocketBalanceBinReader
{
    public static SacredEquipmentSocketBalance Load(string path) => Read(File.ReadAllBytes(path));

    public static SacredEquipmentSocketBalance Read(ReadOnlySpan<byte> file)
    {
        var offset = 0;
        var globals = Take(file, ref offset, Count(file, ref offset));
        if (globals.Length < SacredEquipmentGenerationBalanceLayout.AdditionalSocketChanceOffset + 4)
            throw new InvalidDataException("Balance.bin omits socket tuning.");
        var vectors = Count(file, ref offset);
        var maximums = new Dictionary<uint, int>();
        if (vectors > (file.Length - offset) / 8) throw new InvalidDataException("Truncated Balance.bin socket pools.");
        for (var i = 0; i < vectors; i++)
        {
            var key = BinaryPrimitives.ReadUInt32LittleEndian(Take(file, ref offset, 4));
            var rows = Count(file, ref offset);
            if (rows > (file.Length - offset) / SacredEquipmentGenerationBalanceLayout.BonusPoolEntrySize)
                throw new InvalidDataException("Truncated Balance.bin socket pool.");
            var records = Take(file, ref offset, rows * SacredEquipmentGenerationBalanceLayout.BonusPoolEntrySize);
            maximums[key] = rows == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(records[SacredEquipmentGenerationBalanceLayout.FirstPoolEntryMaximumSocketsOffset..]);
        }
        Skip(file, ref offset, 8); // Second map, not the quality-budget map.
        var count = Count(file, ref offset);
        if (count > (file.Length - offset) / SacredEquipmentGenerationBalanceLayout.QualityBudgetEntrySize)
            throw new InvalidDataException("Truncated Balance.bin quality budgets.");
        var budgets = new Dictionary<uint, uint>();
        for (var i = 0; i < count; i++)
        {
            var pair = Take(file, ref offset, SacredEquipmentGenerationBalanceLayout.QualityBudgetEntrySize);
            budgets[BinaryPrimitives.ReadUInt32LittleEndian(pair)] = BinaryPrimitives.ReadUInt32LittleEndian(pair[4..]);
        }
        var costs = new float[3];
        for (var i = 0; i < costs.Length; i++) costs[i] = BinaryPrimitives.ReadSingleLittleEndian(globals[(SacredEquipmentGenerationBalanceLayout.SocketCostFactorsOffset + i * 4)..]);
        return new(BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredEquipmentGenerationBalanceLayout.FirstSocketChanceOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredEquipmentGenerationBalanceLayout.AdditionalSocketChanceOffset..]), costs, budgets, maximums);
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int offset, int stride)
    {
        var count = Count(data, ref offset);
        if (count > (data.Length - offset) / stride) throw new InvalidDataException("Truncated Balance.bin table.");
        Take(data, ref offset, count * stride);
    }
    private static int Count(ReadOnlySpan<byte> data, ref int offset)
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(Take(data, ref offset, 4));
        if (count > int.MaxValue) throw new InvalidDataException("Invalid Balance.bin count.");
        return (int)count;
    }
    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        if (length < 0 || length > data.Length - offset) throw new InvalidDataException("Truncated Balance.bin prefix.");
        var result = data.Slice(offset, length);
        offset += length;
        return result;
    }
}
