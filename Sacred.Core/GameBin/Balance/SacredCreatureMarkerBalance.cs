namespace Sacred.Core.GameBin.Balance;

/// <summary>Original health-ring colours and relative-level thresholds from Balance.bin.</summary>
public sealed record SacredCreatureMarkerBalance(uint PlayerHealthColor, uint PlayerBorderColor,
    float MinimumLevelRatio, float RedLevelRatio, float FullExperienceLevelRatio)
{
    public uint LevelColor(int level, int heroLevel)
    {
        var ratio = (level + 9f) / (heroLevel + 9);
        var lower = heroLevel < 11 ? (heroLevel + 6f) / (heroLevel + 9) :
            heroLevel > 91 ? (heroLevel - 6f) / (heroLevel + 9) : MinimumLevelRatio;
        if (ratio <= lower) return 0x808080;
        var t = (ratio - lower) / (FullExperienceLevelRatio - lower);
        if (t <= 0.04) return 0x808080;
        if (ratio > RedLevelRatio) return 0xFF0000;
        if (ratio >= FullExperienceLevelRatio)
        {
            t = (ratio - FullExperienceLevelRatio) / (RedLevelRatio - FullExperienceLevelRatio);
            return (uint)(220 + t * 35) << 16 | (uint)((1 - t) * 240) << 8 | (uint)((1 - t) * 15);
        }
        return (uint)(43 - (int)(t * -177)) << 16 | (uint)(221 - (int)(t * -19)) << 8 |
               (uint)(15 - (int)((1 - t) * -23));
    }
}
