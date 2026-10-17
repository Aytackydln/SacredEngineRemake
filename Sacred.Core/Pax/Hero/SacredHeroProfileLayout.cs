namespace Sacred.Core.Pax.Hero;

/// <summary>Gold AMH chunk C3, copied into the native 556-byte network profile.</summary>
public static class SacredHeroProfileLayout
{
    public const uint ChunkType = 0xC3;
    public const int Size = 556;
    public const int LevelOffset = 0;
    public const int Word02Offset = 2;
    public const int Word04Offset = 4;
    /// <summary>Native LoadHero passes profile +8 to the UTF-16 string reader at 0x606B76.</summary>
    public const int NameOffset = 8;
}
