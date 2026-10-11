using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed record SacredControlledFlareSnapshot(SacredRevivalFlareStateLayout Revival,
    SacredDeathCallFlareStateLayout DeathCall, SacredToggleTrapFlareStateLayout Trap,
    ushort Flags, uint Notification);
