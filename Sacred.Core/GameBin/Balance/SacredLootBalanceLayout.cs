namespace Sacred.Core.GameBin.Balance;

/// <summary>Loot fields in Balance.bin's counted global block (Gold 0xAD4490).
/// Chance checks are sequential; these values are not category weights.</summary>
public static class SacredLootBalanceLayout
{
    /// <summary>0x559BB5: scale for the rune drop threshold.</summary>
    public const int RuneChanceFactorOffset = 0x6F8;
    /// <summary>0x559DA0: initial chance budget per thousand. Values above
    /// one thousand allow multiple outer passes; this is not a percentage.</summary>
    public const int CreatureBaseChanceOffset = 0x700;
    public const int CreatureBoostedChanceFactorOffset = 0x708;
    /// <summary>0x533CA2: rand in inclusive range 0..1000, then three
    /// equipment detachment attempts before testing the credited killer.</summary>
    public const int DeathEquipmentDetachChanceOffset = 0x7A8;
    public const int CreatureWeaponChanceOffset = 0x89C; // rand()%1000
    public const int CreatureArmorChanceOffset = 0x8A0;
    public const int CreatureMoneyChanceOffset = 0x8A4;
    public const int CreaturePotionChanceOffset = 0x8B4;
    public const int CreatureMoneyMinimumOffset = 0x8AC;
    public const int CreatureMoneyMaximumOffset = 0x8B0;
    /// <summary>0x559AAA..0x559B00: per-hero-class level coefficients.
    /// File order is Wood Elf, Gladiator, Vampiress, Dark Elf, Battle Mage,
    /// Seraphim (also class 7), Daemon, Dwarf.</summary>
    public const int RuneClassLevelFactorsOffset = 0x810;
    public const int RuneClassLevelFactorCount = 8;
    /// <summary>0x559A30: each carried/stored rune contributes 1/divisor to
    /// the learned combat-art rank sum used as the denominator.</summary>
    public const int RuneInventoryDivisorOffset = 0x11A4;
    public const int RuneBoostedFactorOffset = 0x11A8;
    public const int RuneRatioLimitOffset = 0x11AC;
    /// <summary>0x533D10: default calls to 0x559BF0 on an eligible death.</summary>
    public const int CreatureDeathBaseRollCountOffset = 0x11E8;
    /// <summary>0x533DB3 / 0x533DEB: inclusive upper bounds for extra calls
    /// when the level penalty permits them. The lower bound is one.</summary>
    public const int CreatureMarkedExtraRollMaximumOffset = 0x1250;
    public const int CreatureBoostedExtraRollMaximumOffset = 0x1254;
    public const int CreatureBoostedRarityBonusOffset = 0x1258;
    public const int CreatureMarkedRarityBonusOffset = 0x125C;
    /// <summary>0x533E3D..0x53458A: eleven signed DWORD rarity bonus additions
    /// selected by the original boosted-source resource dispatch.</summary>
    public const int CreatureDeathRewardBonusesOffset = 0x1224;
    public const int CreatureDeathRewardBonusCount = 11;
    /// <summary>0x551530: lower interpolation ratio, upper penalty threshold,
    /// and start of the full-value plateau, respectively.</summary>
    public const int LevelRatioLowerOffset = 0x9A8;
    public const int LevelRatioUpperOffset = 0x9AC;
    public const int LevelRatioPlateauOffset = 0x9B0;
    /// <summary>0x559BF0 / 0x5D57C0: floor blended into rarity weight scaling.</summary>
    public const int RarityWeightFloorOffset = 0x1218;
    /// <summary>0x5D5B29 / 0x5345E9: multiplier on the survival-time curve
    /// added to container score / death caller source bonus.</summary>
    public const int SurvivalRarityFactorOffset = 0x121C;
    /// <summary>0x559E94: extra chance per eligible nearby party member, capped
    /// at four members by the caller. Network eligibility is separate.</summary>
    public const int CreaturePartyMemberChanceFactorOffset = 0x126C;
    /// <summary>0x559FE2: rescale the caller's rarity argument using
    /// sqrt(creature info +0x0A * scale) / scale before each selection attempt.</summary>
    public const int CreatureExperienceBonusScaleOffset = 0x1318;
    public const int RequiredGlobalBytes = CreatureExperienceBonusScaleOffset + sizeof(float);
}
