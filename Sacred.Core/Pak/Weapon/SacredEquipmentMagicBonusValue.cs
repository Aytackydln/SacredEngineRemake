using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D1660 affordability and 0x5D1370 concrete bonus increments.</summary>
public static class SacredEquipmentMagicBonusValue
{
    public static bool CanAfford(uint code, uint budget, bool jewelry, byte level, SacredEquipmentBonusBalance curves)
    {
        var curve = curves.Get((ushort)code);
        var factor = jewelry ? curve.JewelryNormalizationFactor : 1f;
        var divisor = (code & 0xFFFF0000) != 0 ? curve.PercentageCostDivisor : curve.FlatCostDivisor;
        if (divisor <= (double).01f) return true;
        var minimum = (code & 0xFFFF0000) == 0 && curve.LevelScaling >= (double)1e-5f
            ? 1 / ((level + 9) * (double)divisor) / factor : 1 / (divisor / (double)factor);
        return budget >= minimum;
    }

    public static int Add(Span<byte> values, int slot, int budget, SacredItemCategory category, uint difficulty,
        SacredEquipmentBonusBalance curves, SacredEquipmentNormalizationBalance factors)
    {
        if ((uint)slot >= 8) return 0;
        var type = BinaryPrimitives.ReadUInt16LittleEndian(values[(58 + slot * 2)..]);
        var group = BinaryPrimitives.ReadUInt32LittleEndian(values[(74 + slot * 4)..]);
        var curve = curves.Get((ushort)group);
        var modifier = (type & 0xF00) != 0 ? factors.HeroClassFactor : 1f;
        if ((type & 255) != 0) modifier *= (type & 255) == 1 ? factors.HeroOpponentFactor : factors.OtherOpponentFactor;
        double factor = modifier;
        if (category is SacredItemCategory.Ring or SacredItemCategory.Amulet) factor *= curve.JewelryNormalizationFactor;
        factor *= difficulty switch { 1 => .7518796920776367f, 2 => .6024096608161926f, 3 => .5f, 4 => .42918455600738525f, _ => 1f };
        var at = 106 + slot * 2;
        var current = BinaryPrimitives.ReadInt16LittleEndian(values[at..]);
        if (curve.PercentageCostDivisor <= (double).01f && curve.FlatCostDivisor <= (double).01f)
        {
            if (current == 0) BinaryPrimitives.WriteInt16LittleEndian(values[at..], 1);
            return 0;
        }
        double divisor = curve.PercentageCostDivisor, maximum = curve.PercentageMaximum;
        if (divisor <= (double).01f)
        {
            divisor = curve.FlatCostDivisor;
            maximum = curve.FlatMaximum;
            if (curve.LevelScaling >= (double)1e-5f)
            {
                divisor *= values[25] + 9;
                maximum = (values[25] + 9) * (double)curve.LevelScaling;
                if (current > (int)(maximum / factor)) current = unchecked((short)(int)(maximum / factor));
            }
        }
        var cost = 0;
        if (divisor > (double).01f)
        {
            var amount = (float)(divisor * budget / factor);
            var increment = Math.Max(1, Math.Min(maximum / factor, amount) + .5);
            current = unchecked((short)(int)(current + increment));
            cost = (int)(current / divisor * factor + .5);
        }
        if (current == 0) current = 1;
        BinaryPrimitives.WriteInt16LittleEndian(values[at..], current);
        return cost;
    }
}
