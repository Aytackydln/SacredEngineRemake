using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D4314..0x5D4633's flat and multiplicative bonus contributions.</summary>
internal static class SacredEquipmentPriceBonuses
{
    public static (uint Flat, float Multiplier) Calculate(ReadOnlySpan<byte> instance, bool jewelry, SacredEquipmentPricingBalance balance,
        SacredEquipmentBonusBalance curves, uint flags)
    {
        uint flat = 0;
        var multiplier = 1f;
        for (var i = 0; i < 8; i++)
        {
            var group = BinaryPrimitives.ReadUInt32LittleEndian(instance[(74 + i * 4)..]);
            var code = unchecked((ushort)group);
            if (code == 0) continue;
            var amount = BinaryPrimitives.ReadInt16LittleEndian(instance[(106 + i * 2)..]);
            // Native special-code dispatch resets this accumulator before
            // processing each 840..869 bonus (0x5D43A1).
            if (code is >= 840 and < 870) multiplier = 1f;
            var curve = curves.Get(code);
            if ((group & 0xFFFF0000) == 0 && curve.FlatMaximum <= (double)1.1920929e-7f)
                flat = unchecked(flat + (uint)Math.Max(0, BasePrice(code, amount, balance)));
            else
            {
                var factor = (jewelry ? curve.JewelryNormalizationFactor : 1d) * SacredEquipmentBonusNormalization.DifficultyFactor(flags);
                var value = amount / (curve.FlatCostDivisor / factor);
                value = Math.Min(value, curve.FlatMaximum);
                multiplier = (float)(multiplier + unchecked((int)SacredEquipmentPricing.Truncate(value)) * (double).1f);
            }
        }
        return (flat, multiplier);
    }

    private static int BasePrice(ushort code, short amount, SacredEquipmentPricingBalance balance)
    {
        var coefficient = code < 200 ? balance.CombatArtRankPrice : code > 999 ? balance.SpellRankPrice :
            code is >= 600 and < 799 ? balance.SkillRankPrice : code is >= 815 and < 820 ? balance.AttributeRankPrice : balance.OtherRankPrice;
        if (code is < 840 or >= 870) return unchecked((int)SacredEquipmentPricing.Truncate(amount * (double)coefficient));
        var (index, scales) = code switch
        {
            840 => (0, true), 841 => (1, false), 842 => (2, false), 843 => (3, false), 844 => (18, true),
            845 => (17, true), 846 => (4, false), 847 => (19, true), 848 => (20, true), 849 => (16, true),
            850 => (21, false), 851 => (22, false), 852 => (5, true), 853 => (6, false), 854 => (7, false),
            855 => (8, false), 856 => (9, false), 857 => (10, false), 859 => (11, false),
            864 => (15, false), 865 => (12, false), 866 => (13, true), 867 => (14, false), _ => (-1, false)
        };
        return index < 0 ? 0 : scales ? unchecked(balance.SpecialBonusPrices[index] * amount) : balance.SpecialBonusPrices[index];
    }
}
