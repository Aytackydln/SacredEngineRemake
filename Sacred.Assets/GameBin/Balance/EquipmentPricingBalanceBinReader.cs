using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class EquipmentPricingBalanceBinReader
{
    public static SacredEquipmentPricingBalance Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Truncated Balance.bin header.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (size > file.Length - 4 || size < SacredEquipmentPricingBalanceLayout.RequiredGlobalBytes)
            throw new InvalidDataException("Balance.bin omits equipment pricing tuning.");
        var globals = file.Slice(4, checked((int)size)).ToArray();
        var prices = new int[SacredEquipmentPricingBalanceLayout.SpecialBonusPriceCount];
        for (var i = 0; i < prices.Length; i++)
            prices[i] = BinaryPrimitives.ReadInt32LittleEndian(globals.AsSpan(SacredEquipmentPricingBalanceLayout.SpecialBonusPricesOffset + i * 4));
        return new(Curve(SacredEquipmentPricingBalanceLayout.DamagePriceCurveOffset), Curve(SacredEquipmentPricingBalanceLayout.ProtectionPriceCurveOffset),
            Curve(SacredEquipmentPricingBalanceLayout.AttackPriceCurveOffset), Curve(SacredEquipmentPricingBalanceLayout.DefensePriceCurveOffset),
            F(SacredEquipmentPricingBalanceLayout.CombatArtRankPriceOffset), F(SacredEquipmentPricingBalanceLayout.SpellRankPriceOffset),
            F(SacredEquipmentPricingBalanceLayout.SkillRankPriceOffset), F(SacredEquipmentPricingBalanceLayout.AttributeRankPriceOffset), F(SacredEquipmentPricingBalanceLayout.OtherRankPriceOffset),
            Array.AsReadOnly(new[] { F(SacredEquipmentPricingBalanceLayout.PhysicalDamageWeightOffset), F(SacredEquipmentPricingBalanceLayout.FireDamageWeightOffset), F(SacredEquipmentPricingBalanceLayout.MagicDamageWeightOffset), F(SacredEquipmentPricingBalanceLayout.PoisonDamageWeightOffset) }),
            Array.AsReadOnly(new[] { F(SacredEquipmentPricingBalanceLayout.PhysicalProtectionWeightOffset), F(SacredEquipmentPricingBalanceLayout.FireProtectionWeightOffset), F(SacredEquipmentPricingBalanceLayout.MagicProtectionWeightOffset), F(SacredEquipmentPricingBalanceLayout.PoisonProtectionWeightOffset) }),
            Array.AsReadOnly(prices), F(SacredEquipmentPricingBalanceLayout.FinalPriceFactorOffset));

        float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(globals.AsSpan(offset));
        SacredEquipmentPriceCurve Curve(int offset) => new(F(offset), F(offset + 4), F(offset + 8));
    }
}
