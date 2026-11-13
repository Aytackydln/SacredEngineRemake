namespace Sacred.Network.Gameplay;

/// <summary>Original full-object particle snapshot framing, written by the
/// stargate network serializer 0x780A80 and base serializer 0x768EA0.
/// MAGICPRISON 0x79CAD0 starts with size only; its end/base markers are shared.
/// Other particle families can have additional nested blocks.</summary>
public static class GoldParticleSnapshotLayout
{
    public const uint DerivedMarker = 0xC0DEDEAD;
    public const uint DerivedEndMarker = 0xDEADC0DE;
    public const uint BaseMarker = 0xF00DBEEF;
    public const int DerivedSizeOffset = 4;
    public const int SizeOnlyLengthOffset = 0;
    public const int SizeOnlyDataOffset = 4;
    public const int DerivedDataOffset = 8;
    public const int BaseResourceOffset = 4;
    public const int BasePositionOffset = 8;
    public const int BaseHeaderSize = 20;
}
