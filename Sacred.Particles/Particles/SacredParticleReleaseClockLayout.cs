using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Native base-system fields at +0x2080, read by stdCreation 0x764760.
/// Movement populates the free-slot pointer list and count before timed births can use it.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x14)]
public readonly struct SacredParticleReleaseClockLayout
{
    [FieldOffset(0x00)] public readonly uint FreeSlotCount;
    /// <summary>Zero selects single-birth initialization; otherwise elapsed minus this time selects catch-up births.</summary>
    [FieldOffset(0x04)] public readonly float LastReleaseTime;
    [FieldOffset(0x08)] public readonly SacredParticleVectorLayout PreviousEmissionPosition;
}
