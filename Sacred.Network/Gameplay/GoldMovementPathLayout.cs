namespace Sacred.Network.Gameplay;

/// <summary>Path interpretation of creature +0x2ED's 173-byte union, in saves,
/// full creature streams and 0x163. Gold 0x4F75F0 and 0x5953EB..0x59579B.</summary>
public static class GoldMovementPathLayout
{
    public const int Capacity = 16;
    public const int IndexOffset = 128;
    public const int CountOffset = 132;
    public const int SpeedOffset = 136; // +0x375: integer native units per second, after all factors.
    public const int DistanceFractionOffset = 140; // +0x379: Q16 fractional distance.
    public const int RemainderXOffset = 144; // +0x37D: signed Q10 coordinate remainder.
    public const int RemainderYOffset = 148; // +0x381.
    public const int LastClockOffset = 152; // +0x385: movement producer's clock.
    public const int ElapsedOffset = 156; // +0x389: accumulated movement seconds.
    public const int TargetXOffset = 164; // +0x391.
    public const int TargetYOffset = 168; // +0x395.
}
