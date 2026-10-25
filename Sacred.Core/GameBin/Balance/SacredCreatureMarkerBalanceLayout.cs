namespace Sacred.Core.GameBin.Balance;

/// <summary>Counted Balance.bin global block, native base AD4490; Gold 599B71–599E3E.</summary>
public static class SacredCreatureMarkerBalanceLayout
{
    public const int PlayerHealthColorOffset = 0x8D8;
    public const int PlayerBorderColorOffset = 0x8DC;
    public const int MinimumLevelRatioOffset = 0x9A8;
    public const int RedLevelRatioOffset = 0x9AC;
    public const int FullExperienceLevelRatioOffset = 0x9B0;
    public const int RequiredGlobalBytes = FullExperienceLevelRatioOffset + 4;
}
