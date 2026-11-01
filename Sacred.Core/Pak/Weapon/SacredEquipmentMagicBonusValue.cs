using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D1660 affordability and 0x5D1370 concrete bonus increments.</summary>
public static class SacredEquipmentMagicBonusValue
{
    public static bool CanAfford(uint code, uint budget, bool jewelry, byte level, SacredEquipmentBonusBalance curves,
        SacredX87Precision precision = SacredX87Precision.Extended)
    {
        var fp = new SacredResultArithmetic(precision);
        var curve = curves.Get((ushort)code);
        var factor = jewelry ? curve.JewelryNormalizationFactor : 1f;
        var divisor = (code & 0xFFFF0000) != 0 ? curve.PercentageCostDivisor : curve.FlatCostDivisor;
        if (divisor <= (double).01f) return true;
        var minimum = (code & 0xFFFF0000) == 0 && curve.LevelScaling >= (double)1e-5f
            ? fp.Divide(fp.Divide(1, fp.Multiply(level + 9, divisor)), factor) : fp.Divide(1, fp.Divide(divisor, factor));
        return budget >= minimum;
    }

    public static int Add(Span<byte> values, int slot, int budget, SacredItemCategory category, uint difficulty,
        SacredEquipmentBonusBalance curves, SacredEquipmentNormalizationBalance factors, SacredX87Precision precision = SacredX87Precision.Extended)
    {
        if ((uint)slot >= 8) return 0;
        var fp = new SacredResultArithmetic(precision);
        var type = BinaryPrimitives.ReadUInt16LittleEndian(values[(58 + slot * 2)..]);
        var group = BinaryPrimitives.ReadUInt32LittleEndian(values[(74 + slot * 4)..]);
        var curve = curves.Get((ushort)group);
        var modifier = (type & 0xF00) != 0 ? factors.HeroClassFactor : 1f;
        if ((type & 255) != 0) modifier = (float)fp.Multiply(modifier, (type & 255) == 1 ? factors.HeroOpponentFactor : factors.OtherOpponentFactor);
        double factor = modifier;
        if (category is SacredItemCategory.Ring or SacredItemCategory.Amulet) factor = fp.Multiply(factor, curve.JewelryNormalizationFactor);
        factor = fp.Multiply(factor, difficulty switch { 1 => .7518796920776367f, 2 => .6024096608161926f, 3 => .5f, 4 => .42918455600738525f, _ => 1f });
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
                divisor = fp.Multiply(divisor, values[25] + 9);
                maximum = fp.Multiply(values[25] + 9, curve.LevelScaling);
                if (current > (int)fp.Divide(maximum, factor)) current = unchecked((short)(int)fp.Divide(maximum, factor));
            }
        }
        var cost = 0;
        if (divisor > (double).01f)
        {
            var amount = (float)fp.Divide(fp.Multiply(divisor, budget), factor);
            var increment = Math.Max(1, fp.Add(Math.Min(fp.Divide(maximum, factor), amount), .5));
            current = unchecked((short)(int)fp.Add(current, increment));
            cost = (int)fp.Add(fp.Multiply(fp.Divide(current, divisor), factor), .5);
        }
        if (current == 0) current = 1;
        BinaryPrimitives.WriteInt16LittleEndian(values[at..], current);
        return cost;
    }
}
