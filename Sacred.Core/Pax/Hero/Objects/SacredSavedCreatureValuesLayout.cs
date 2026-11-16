namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Gold file writer 0x585260. The third runtime 32-byte vector serializes 28 bytes per record.</summary>
public static class SacredSavedCreatureValuesLayout
{
    public const int MarkerOffset = 0;
    public const int Block128Offset = 4;
    public const int Block128Size = 24;
    /// <summary>Gold TP[1]/TP[2]: maximum/current signed HP, outside the 238-byte stat block.</summary>
    public const int MaximumHitPointsOffset = Block128Offset + 4;
    public const int CurrentHitPointsOffset = Block128Offset + 8;
    public const int Block08Offset = 28;
    public const int Block08Size = 238;
    // Original sCharValHero counters within the +0x08 block; Gold CalcResults retains these offsets.
    public const int ExperienceOffset = Block08Offset + 4;
    public const int RemainingSkillPointsOffset = Block08Offset + 58;
    public const int GoldOffset = Block08Offset + 62;
    public const int LevelOffset = Block08Offset + 78;
    public const int Count22Offset = 266;
    /// <summary>Gold 585514 reads acquired art entries using the same packed
    /// layout as runtime sSpellEntry. The local-hero 585C2C -> 5796A0 pass then
    /// recalculates TimerMaximum; the serialized value is not authoritative.</summary>
    public const int Record22Size = Sacred.Core.CombatArts.SacredCombatArtRuntimeLayout.Size;
    public const int Record38Size = 38;
    /// <summary>Third counted list: active status modifiers, not learned arts or equipment bonuses.</summary>
    public const int Record28Size = SacredSavedStatusModifierLayout.Size;
    public const int MinimumSize = 272;
}
