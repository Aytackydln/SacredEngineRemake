namespace Sacred.Core.Runtime;

/// <summary>Original statistics block at cStats +0x30A4, copied by Gold 0x4250F4.</summary>
public static class SacredHeroStatisticsPrimaryLayout
{
    public const int Size = 0x2670;
    /// <summary>Native +0x570C. Getter 0x424650 returns unsigned milliseconds/1000;
    /// setter 0x424670 stores seconds*1000. CreateNPC divides the getter by 60
    /// before its automatic-level percentage curve. Reset 0x4246D0 clears it;
    /// the wider counter lifecycle is separate from this established use.</summary>
    public const int AutomaticLevelElapsedMillisecondsOffset = 0x2668;
}
