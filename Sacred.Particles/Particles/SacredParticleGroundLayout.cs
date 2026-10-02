using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Recovered base-system fields read by Gold stdMovementWithGround (0x7645C0).
/// In-memory offsets only; the enclosing base-system save record is not yet mapped.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0xA8)]
public readonly struct SacredParticleGroundLayout
{
    [FieldOffset(0x08)] public readonly SacredParticleVectorLayout EmitterPosition;
    /// <summary>Native heightAboveGround. Collision plane = EmitterPosition.Z - this value.
    /// The routine negates vertical velocity or clears energy for movement mode 2.</summary>
    [FieldOffset(0xA4)] public readonly float HeightAboveGround;
}
