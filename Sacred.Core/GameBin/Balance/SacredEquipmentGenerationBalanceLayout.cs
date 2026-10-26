namespace Sacred.Core.GameBin.Balance;

/// <summary>Equipment generation fields in Balance.bin's counted globals (Gold base 0xAD4490).</summary>
public static class SacredEquipmentGenerationBalanceLayout
{
    /// <summary>Gold 0x5D52B2 interpolates these floats using level * native 0.01f.</summary>
    public const int LevelFactorZeroOffset = 0x1FC;
    public const int LevelFactorHundredOffset = 0x200;
    /// <summary>Generation level cap = truncate(216 / scale). Also raises generation level
    /// to meet wpmod.bin +0x9C, after the combat level factor has already been computed.</summary>
    public const int LevelScaleOffset = 0x111C;
    /// <summary>Seventy 24-byte curves for bonus codes 800..869, selected at 0x5E22B0.</summary>
    public const int IndividualBonusCurvesOffset = 0x9DC;
    /// <summary>Gold 0x42C020: nonzero BonusT low byte other than hero class one.</summary>
    public const int OtherOpponentNormalizationFactorOffset = 0x9D0;
    public const int HeroOpponentNormalizationFactorOffset = 0x9D4;
    /// <summary>Gold 0x42BFFC: BonusT mask 0x0F00 selects a hero restriction.</summary>
    public const int HeroClassNormalizationFactorOffset = 0x9D8;
    public const int SpecialMoveBonusCurveOffset = 0x106C;
    public const int SpellBonusCurveOffset = 0x1084;
    public const int SkillBonusCurveOffset = 0x109C;
    public const int OtherBonusCurveOffset = 0x10B4;
    /// <summary>Gold 0x5D2F85 tests rand()%1000 against these UInt32 values for
    /// a first socket and each additional socket, respectively.</summary>
    public const int FirstSocketChanceOffset = 0x11B0;
    public const int AdditionalSocketChanceOffset = 0x11B4;
    /// <summary>Three floats added to the bonus-cost multiplier for socket kinds 1, 2 and 3.</summary>
    public const int SocketCostFactorsOffset = 0x10E4;
    /// <summary>The first counted map after globals contains 12-byte rows keyed by
    /// wpmod WeaponType. The first row's third Int32 is its maximum socket count
    /// (Gold 0x440470 / 0x5D2FA0); remaining row fields need further interpretation.</summary>
    public const int BonusPoolEntrySize = 12;
    public const int FirstPoolEntryMaximumSocketsOffset = 8;
    /// <summary>First counted map rows: inclusive bonus-code ranges at +0/+4;
    /// 0x5D1750 skill membership also accepts equality with the first bound.
    /// A final bound of -1 is retained literally, not expanded to infinity.</summary>
    public const int FirstPoolEntryFirstBonusCodeOffset = 0;
    public const int FirstPoolEntryLastBonusCodeOffset = 4;
    /// <summary>Second counted UInt32 pair map (0xAD57BC), copied into the native
    /// rarity vector by 0x5CF4B0. Distinct from the third magic-budget map.</summary>
    public const int RarityWeightEntrySize = 8;
    /// <summary>After the first vector map and second pair map, the third counted
    /// UInt32 key/value map loads at 0xAD57C8 (0x7120CD..0x712135). It maps both
    /// the quality argument and current rarity tier to a magic budget (0x5D5624 /
    /// 0x5D2EAE); these are not the second map's values.</summary>
    public const int QualityBudgetEntrySize = 8;
    public const int RequiredGlobalBytes = LevelScaleOffset + sizeof(float);
}
