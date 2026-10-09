using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>stdCreationOnLine 0x766200 base state at +0x2080.
/// Both saved endpoints include the emission position offset.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20)]
public readonly struct SacredParticleLineReleaseClockLayout
{
    [FieldOffset(0)] public readonly uint FreeSlotCount;
    [FieldOffset(4)] public readonly float LastReleaseTime;
    [FieldOffset(8)] public readonly SacredParticleVectorLayout PreviousStart;
    [FieldOffset(0x14)] public readonly SacredParticleVectorLayout PreviousEnd;
}
