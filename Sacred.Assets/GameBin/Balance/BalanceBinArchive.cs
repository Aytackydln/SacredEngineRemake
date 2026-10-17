using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

/// <summary>Reads the verified Gold prefix through the movement profiles. Later maps remain uninterpreted.</summary>
public static class BalanceBinArchive
{
    public static SacredMovementBalance Load(string path) => Parse(File.ReadAllBytes(path));

    public static SacredMovementBalance Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        var globals = Take(data, ref offset, Count(data, ref offset));
        if (globals.Length < SacredBalanceLayout.MovementScaleOffset + sizeof(float))
            throw new InvalidDataException("Balance.bin global block omits the movement scale.");
        var tuning = MemoryMarshal.Read<SacredBalanceGlobalsLayout>(globals);
        var scale = tuning.MovementScale;
        if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Invalid Balance.bin movement scale.");
        var vectors = Count(data, ref offset);
        if (vectors > (data.Length - offset) / 8) throw new InvalidDataException("Invalid Balance.bin vector count.");
        for (var i = 0; i < vectors; i++)
        {
            Take(data, ref offset, 4); // Unresolved key.
            SkipRecords(data, ref offset, SacredBalanceLayout.VectorRecordSize);
        }
        SkipRecords(data, ref offset, SacredBalanceLayout.PairRecordSize);
        SkipRecords(data, ref offset, SacredBalanceLayout.PairRecordSize);
        var count = Count(data, ref offset);
        if (count > (data.Length - offset) / SacredBalanceLayout.MovementProfileRecordSize)
            throw new InvalidDataException("Truncated Balance.bin movement profiles.");
        var profiles = new Dictionary<uint, SacredMovementProfileLayout>();
        for (var i = 0; i < count; i++)
        {
            var key = BinaryPrimitives.ReadUInt32LittleEndian(Take(data, ref offset, 4));
            profiles[key] = MemoryMarshal.Read<SacredMovementProfileLayout>(Take(data, ref offset, SacredMovementProfileLayout.Size));
        }
        var armorLore = tuning.ArmorLoreMovement;
        if (!float.IsFinite(armorLore.Base) || !float.IsFinite(armorLore.Divisor) || armorLore.Divisor <= 0 || !float.IsFinite(armorLore.Limit))
            throw new InvalidDataException("Invalid Balance.bin Armor Lore movement curve.");
        return new(scale, profiles, armorLore);
    }

    private static void SkipRecords(ReadOnlySpan<byte> data, ref int offset, int stride)
    {
        var count = Count(data, ref offset);
        if (count > (data.Length - offset) / stride) throw new InvalidDataException("Truncated Balance.bin table.");
        Take(data, ref offset, count * stride);
    }
    private static int Count(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(Take(data, ref offset, 4));
        if (value > int.MaxValue) throw new InvalidDataException("Invalid Balance.bin count.");
        return (int)value;
    }
    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int offset, int size)
    {
        if (size < 0 || size > data.Length - offset) throw new InvalidDataException("Truncated Balance.bin prefix.");
        var result = data.Slice(offset, size);
        offset += size;
        return result;
    }
}
