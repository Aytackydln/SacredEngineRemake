using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Base-system scalar fields at +0x98. Demo names userData and lightRadius;
/// Gold Magic Fire/Fireball initializers write these offsets directly.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 8)]
public readonly struct SacredParticleContextLayout
{
    [FieldOffset(0)] public readonly uint UserData;
    /// <summary>Signed native light radius. Fireball writes 250; Magic Fire writes
    /// 200 - trunc(stored strength * -200). Scene illumination is separate from particle draws.</summary>
    [FieldOffset(4)] public readonly int LightRadius;
}
