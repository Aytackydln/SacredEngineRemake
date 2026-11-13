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
    /// <summary>Float at global +0x8C0 (Gold 0xAD4D50). Health 0x565ABA selects
    /// this factor when actor behavior +0x1F4 contains mask 0x80000, before the
    /// original type/class/difficulty multiplier. It is not the final multiplier.</summary>
    public const int BehaviorHealthFactorOffset = 0x8C0;
    /// <summary>Float at global +0x8C4 (Gold 0xAD4D54). Health 0x565AC6 selects
    /// this alternative for status +0x2B8 bit 6 when behavior mask 0x80000 is clear.</summary>
    public const int StatusHealthFactorOffset = 0x8C4;
    public const int DifficultyCount = 6;
    public const int RequiredGlobalBytes = LevelAdditionsOffset + DifficultyCount * sizeof(int);
}
