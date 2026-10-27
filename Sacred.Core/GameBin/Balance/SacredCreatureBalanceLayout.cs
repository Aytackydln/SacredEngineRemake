namespace Sacred.Core.GameBin.Balance;

/// <summary>Offsets in Balance.bin's counted global block, loaded at Gold 0xAD4490.</summary>
public static class SacredCreatureBalanceLayout
{
    /// <summary>Five (level, health factor) float pairs. Gold 0x565BAF interpolates
    /// adjacent pairs, using the last segment for all levels above its lower bound.</summary>
    public const int HealthLevelPairsOffset = 0x2E0;
    public const int HealthLevelPairCount = 5;
    /// <summary>Vitality skill 10: base, divisor, limit (Gold 0x565A45).</summary>
    public const int VitalityCurveOffset = 0x19C;
    /// <summary>Six float multipliers, indexed by the native difficulty selector.</summary>
    public const int HealthDifficultyFactorsOffset = 0x728;
    /// <summary>Six signed percent additions, used by level normalization 0x552230
    /// and creature loot generation 0x55B537. The latter reverses growth without
    /// the health-colour helper's rounding and minimum-level clamp.</summary>
    public const int LevelPercentAdditionsOffset = 0x11B8;
    /// <summary>Six signed level additions, applied after percentage scaling;
    /// 0x55B51B subtracts them from source level before loot generation.</summary>
    public const int LevelAdditionsOffset = 0x11D0;
    public const int DifficultyCount = 6;
    public const int RequiredGlobalBytes = LevelAdditionsOffset + DifficultyCount * sizeof(int);
}
