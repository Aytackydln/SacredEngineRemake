using Sacred.Core.Pax.Hero.Statistics;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x4250F4 writer copies cStats +0x30A4, then +0x5724. The 16-byte native gap is absent on wire.</summary>
public static class GoldPlayerStatisticsLayout
{
    public const ushort Opcode = 0x14F;
    public const int PrimaryOffset = 0;
    public const int PrimarySize = 0x2670;
    public const int ExtensionOffset = PrimarySize;
    public const int ExtensionSize = SacredHeroStatisticsExtensionLayout.Size;
    public const int Size = PrimarySize + ExtensionSize;
}
