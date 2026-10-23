namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x7DF1DC: unsigned integer words, converted to float before the region setter.</summary>
public static class GoldWorldRegionValueLayout
{
    public const ushort SetOpcode = 0x187;
    public const ushort AddOpcode = 0x188;
    public const int Size = 12;
    public const int RegionOffset = 0;
    public const int SelectorOffset = 4;
    public const int ValueOffset = 8;
    public const ushort Status = 1;
    public const ushort RegionFactor = 2;
    public const ushort StoryFactor = 3;
    public const ushort DynamicFactor = 4;
}
