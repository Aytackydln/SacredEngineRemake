using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>stdCreation's common base at object +0x2080, used by Reanimate.
/// The line generator extends this prefix with a second endpoint at +0x2094.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x14)]
public readonly struct SacredParticlePointReleaseClockLayout
{
    [FieldOffset(0)] public readonly uint FreeSlotCount;
    [FieldOffset(4)] public readonly float ReleaseTime;
    [FieldOffset(8)] public readonly SacredParticleVectorLayout ReleasedOrigin;
}
