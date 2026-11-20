namespace Sacred.Core.Pax.Hero;

/// <summary>Gold 0x6066DB reads 64 bytes of C4 and restores the local player's statistics.</summary>
public static class SacredHeroStartInfoLayout
{
    public const int Size = 64;
    public const int Word56F4Offset = 0;
    public const int Word56F0Offset = 4;
    public const int AutomaticLevelElapsedSecondsOffset = 8;
    public const int Word5710Offset = 12;
    public const int StatisticsTripletOffset = 16;
    public const int LegacyStatisticsTripletOffset = 32;
}
