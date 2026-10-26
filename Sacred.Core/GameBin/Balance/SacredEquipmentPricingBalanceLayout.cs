namespace Sacred.Core.GameBin.Balance;

/// <summary>Gold 0x5D3AB0 fields in Balance.bin's counted global block (base 0xAD4490).</summary>
public static class SacredEquipmentPricingBalanceLayout
{
    /// <summary>Three float coefficients: constant, linear, quadratic.</summary>
    public const int DamagePriceCurveOffset = 0x204;
    public const int ProtectionPriceCurveOffset = 0x210;
    public const int CombatArtRankPriceOffset = 0x21C;
    public const int SpellRankPriceOffset = 0x220;
    public const int SkillRankPriceOffset = 0x224;
    public const int AttributeRankPriceOffset = 0x22C;
    public const int OtherRankPriceOffset = 0x230;
    public const int AttackPriceCurveOffset = 0x268;
    public const int DefensePriceCurveOffset = 0x274;
    public const int PhysicalDamageWeightOffset = 0x2C8;
    public const int FireDamageWeightOffset = 0x280;
    public const int MagicDamageWeightOffset = 0x298;
    public const int PoisonDamageWeightOffset = 0x2B0;
    public const int PhysicalProtectionWeightOffset = 0x2D4;
    public const int FireProtectionWeightOffset = 0x28C;
    public const int MagicProtectionWeightOffset = 0x2A4;
    public const int PoisonProtectionWeightOffset = 0x2BC;
    /// <summary>23 Int32 prices selected by the native 840..867 bonus-code switch.
    /// Some are fixed amounts; others multiply the signed bonus magnitude.</summary>
    public const int SpecialBonusPricesOffset = 0x900;
    public const int SpecialBonusPriceCount = 23;
    /// <summary>Applied to the final UInt32 price before requirement mutation.</summary>
    public const int FinalPriceFactorOffset = 0x11EC;
    public const int RequiredGlobalBytes = FinalPriceFactorOffset + sizeof(float);
}
