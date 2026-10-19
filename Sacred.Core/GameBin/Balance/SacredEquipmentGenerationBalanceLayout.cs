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
    public const int RequiredGlobalBytes = LevelScaleOffset + sizeof(float);
}
