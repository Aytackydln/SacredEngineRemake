using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

public readonly record struct SacredEquipmentNormalizationResult(uint Rarity, int? Cost);

/// <summary>Gold 0x42BF40: normalize concrete bonus magnitudes and derive rarity
/// from their cost. Neither requirements nor price are calculated here.</summary>
public static class SacredEquipmentBonusNormalization
{
    public static SacredEquipmentNormalizationResult Apply(Span<byte> instance, SacredItemCategory category,
        SacredEquipmentBonusBalance curves, SacredEquipmentNormalizationBalance factors, SacredEquipmentSocketBalance sockets)
    {
        if (instance.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected 130-byte item values.", nameof(instance));
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(sockets);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]);
        if ((flags & 0xF0000) == 0xF0000) return new(15, null);
        var jewelry = category is SacredItemCategory.Ring or SacredItemCategory.Amulet;
        var total = 0f;
        for (var i = 0; i < 8; i++)
        {
            var group = BinaryPrimitives.ReadUInt32LittleEndian(instance[(74 + i * 4)..]);
            var code = unchecked((ushort)group);
            if (code == 0 || i == 0 && !jewelry && code is not (>= 840 and <= 870)) continue;
            var type = BinaryPrimitives.ReadUInt16LittleEndian(instance[(58 + i * 2)..]);
            var curve = curves.Get(code);
            double factor = (type & 0xF00) == 0 ? 1 : factors.HeroClassFactor;
            if ((byte)type != 0) factor *= (byte)type == 1 ? factors.HeroOpponentFactor : factors.OtherOpponentFactor;
            if (jewelry) factor *= curve.JewelryNormalizationFactor;
            factor *= DifficultyFactor(flags);
            var value = BinaryPrimitives.ReadInt16LittleEndian(instance[(106 + i * 2)..]);
            double divisor;
            if ((group & 0xFF0000) != 0)
            {
                value = Clamp(value, curve.PercentageMaximum, factor);
                divisor = curve.PercentageCostDivisor;
            }
            else if (curve.LevelScaling < (double)1e-5f)
            {
                value = Clamp(value, curve.FlatMaximum, factor);
                divisor = curve.FlatCostDivisor;
            }
            else
            {
                var maximum = (instance[25] + 9) * (double)curve.LevelScaling / factor;
                if (value > Truncate(maximum)) value = unchecked((short)Truncate(maximum + .5));
                if (value == 0) value = 1;
                divisor = (instance[25] + 9) * (double)curve.FlatCostDivisor;
            }
            BinaryPrimitives.WriteInt16LittleEndian(instance[(106 + i * 2)..], value);
            if (divisor > (double).01f)
                total = (float)(total + Math.Max(1, value / divisor * factor + .5));
        }
        // Native retains the socket sum in x87 until the final single store.
        double multiplier = 1;
        for (var i = 0; i < 8; i++)
        {
            var kind = instance[12 + i];
            if (kind != 0) multiplier += sockets.Costs[kind == 1 ? 0 : kind == 2 ? 1 : 2];
        }
        total = (float)(total * multiplier);
        uint rarity = 14;
        var index = 0;
        foreach (var pair in sockets.QualityBudgets.OrderBy(pair => pair.Key))
        {
            if (pair.Value >= (double)total) { rarity = (uint)Math.Max(0, index - 1); break; }
            index++;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(instance[4..], (flags & 0xFFF0FFFF) | (rarity << 16));
        return new(rarity, Truncate(total));
    }

    /// <summary>Gold reads the item's generation-difficulty nibble, not current session difficulty.</summary>
    internal static float DifficultyFactor(uint flags) => ((flags >> 24) & 15) switch
    {
        2 => .7518796920776367f, 3 => .6024096608161926f, 4 => .5f, 5 => .42918455600738525f, _ => 1
    };

    private static short Clamp(short value, float maximum, double factor)
    {
        if (Truncate(maximum) / factor < value) value = unchecked((short)Truncate(maximum / factor + .5));
        return value == 0 ? (short)1 : value;
    }
    private static int Truncate(double value) => unchecked((int)(long)value);
}
