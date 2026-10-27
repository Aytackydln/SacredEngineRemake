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
    public SacredRuneLootBalance? Runes { get; init; }
    public int CreatureMoneyMinimum { get; init; }
    public int CreatureMoneyMaximum { get; init; }
    public uint CreatureDeathBaseRollCount { get; init; }
    public int CreatureMarkedExtraRollMaximum { get; init; }
    public int CreatureBoostedExtraRollMaximum { get; init; }
    public uint CreatureBoostedRarityBonus { get; init; }
    public uint CreatureMarkedRarityBonus { get; init; }
    public int DeathEquipmentDetachChance { get; init; }
    public IReadOnlyList<uint>? DeathRewardBonuses { get; init; }
}
