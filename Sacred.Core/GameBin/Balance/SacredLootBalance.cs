namespace Sacred.Core.GameBin.Balance;

public sealed record SacredLootBalance(SacredCreatureBalance CreatureLevels,
    float LevelRatioLower, float LevelRatioUpper, float LevelRatioPlateau,
    float RarityWeightFloor, float SurvivalRarityFactor,
    int CreatureWeaponChance, int CreatureArmorChance, int CreatureMoneyChance, int CreaturePotionChance)
{
    public float CreatureBaseChance { get; init; }
    public float CreatureBoostedChanceFactor { get; init; }
    public float CreaturePartyMemberChanceFactor { get; init; }
    public float CreatureExperienceBonusScale { get; init; }
}
