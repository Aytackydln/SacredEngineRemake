using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Saved native bytes plus base controls and the caller's CRT random stream.</summary>
public sealed record SacredPulseFlareSnapshot(SacredTransdimFxStateLayout Transdim,
    SacredPongBallFxStateLayout PongBall, SacredZhadumFxStateLayout Zhadum,
    ushort Flags, uint Notification, uint Speed, uint RandomState);
