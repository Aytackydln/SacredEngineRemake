using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Embedded native +0x3A8 writer 0x585C60. Offsets are relative to its own marker.</summary>
public static class GoldCreatureValuesLayout
{
    public const uint Marker = 0xDEADBEEF;
    public const int Block128Offset = 4;
    public const int Block128Size = 24;
    public const int MaximumHitPointsOffset = SacredSavedCreatureValuesLayout.MaximumHitPointsOffset;
    public const int CurrentHitPointsOffset = SacredSavedCreatureValuesLayout.CurrentHitPointsOffset;
    /// <summary>Native TP[4], +0x138: CalcResults initializes it from twice the
    /// grown archive +0x0F attribute for an ordinary owned creature.</summary>
    public const int Word138Offset = Block128Offset + 16;
    public const int CompactCount22Offset = 54;
    public const int CompactSuffixSize = 17;
    public const int CompactMinimumSize = 75;
    public const int FullBlock08Offset = 28;
    public const int FullBlock08Size = 238;
    public const int FullCount22Offset = 266;
    public const int FullSuffixSize = 4;
    public const int FullMinimumSize = 274;
    public const int Record22Size = 22;
    public const int Record38Size = 38;
    public const int CompactWord0COffset = 28;
    public const int CompactBlock24Offset = 32;
    public const int CompactBlock2COffset = 40;
    public const int CompactWord46Offset = 48;
    public const int CompactWord56Offset = 52;

    /// <summary>The original switch selects the full +0x08 block only for 0x16B.</summary>
    public static bool IsFull(ushort opcode) => opcode switch
    {
        0x16B => true,
        0x11B or 0x169 or 0x16C or 0x16E or 0x16F => false,
        _ => throw new ArgumentOutOfRangeException(nameof(opcode), "Unsupported Gold creature values opcode.")
    };
}
