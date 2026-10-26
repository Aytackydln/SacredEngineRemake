namespace Sacred.Core.GameBin.Balance;

/// <summary>Balance.bin global-block offsets read by Gold SetLevel (0x564D60).</summary>
public static class SacredCreatureLevelBalanceLayout
{
    /// <summary>Six unsigned additions applied before difficulty normalization.
    /// Entry four aliases MultiplayerAddition; the original storage overlaps.</summary>
    public const int InputAdditionsOffset = 0x788;
    public const int MultiplayerAdditionOffset = 0x798;
    /// <summary>Additional sector cap for behavior +0x1F4 mask 0x80000,
    /// unless Creature.pak Flags bit 1 excludes the cap.</summary>
    public const int BehaviorCapAdditionOffset = 0x8B8;
    /// <summary>Additional sector cap for actor +0x2B8 mask 0x40, with the same exclusion.</summary>
    public const int ActorCapAdditionOffset = 0x8BC;
    /// <summary>Six unsigned maximum levels; a nonzero sector maximum is added first.</summary>
    public const int MaximumLevelsOffset = 0x98C;
    public const int RequiredGlobalBytes = MaximumLevelsOffset + SacredCreatureBalanceLayout.DifficultyCount * sizeof(uint);
}
