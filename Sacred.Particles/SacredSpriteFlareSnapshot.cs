using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed record SacredSpriteFlareSnapshot(SacredBfgFxStateLayout Bfg,
    SacredTrap02FxStateLayout Trap, ushort Flags, uint Notification, float GroundFlareHeightOffset);
