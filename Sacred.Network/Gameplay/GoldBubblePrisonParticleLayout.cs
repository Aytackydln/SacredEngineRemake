namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x79C6B0 copies native +0x20A0..+0x211F. Size-only framing
/// precedes this block; no opening derived marker. Updates rotate ten rings and
/// grow the last height until its randomized reset threshold.</summary>
public static class GoldBubblePrisonParticleLayout
{
    public const int DerivedSize = 0x80;
    public const int ElapsedOffset = 0;
    public const int ActiveOffset = 4;
    /// <summary>Transmitted backing at native +0x20A5; compared update/render
    /// paths ignore these three bytes.</summary>
    public const int BackingOffset = 5;
    public const int BackingSize = 3;
    public const int AnglesOffset = 8;
    public const int AngularVelocitiesOffset = 0x30;
    public const int HeightsOffset = 0x58;
    public const int RingCount = 10;
}
