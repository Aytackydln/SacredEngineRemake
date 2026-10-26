namespace Sacred.Core.GameBin.Balance;

public readonly record struct SacredEquipmentPriceCurve(float Constant, float Linear, float Quadratic)
{
    public double Evaluate(double value) => (value * Quadratic + Linear) * value + Constant;
}

/// <summary>Original price tuning; item level and requirements remain instance state.</summary>
public sealed record SacredEquipmentPricingBalance(SacredEquipmentPriceCurve Damage, SacredEquipmentPriceCurve Protection,
    SacredEquipmentPriceCurve Attack, SacredEquipmentPriceCurve Defense, float CombatArtRankPrice, float SpellRankPrice,
    float SkillRankPrice, float AttributeRankPrice, float OtherRankPrice, IReadOnlyList<float> DamageWeights,
    IReadOnlyList<float> ProtectionWeights, IReadOnlyList<int> SpecialBonusPrices, float FinalPriceFactor);
