using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Native cParticle (0x40 bytes), confirmed by the executable analysis.
/// This is an in-memory element of cParticleSystem::particles, not a PAK header.
/// Gold's stdMovement (0x7640C0) uses the same member offsets.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x40)]
public readonly struct SacredNativeParticleLayout
{
    [FieldOffset(0x00)] public readonly SacredParticleVectorLayout Position;
    [FieldOffset(0x0C)] public readonly SacredParticleVectorLayout OldPosition;
    [FieldOffset(0x18)] public readonly SacredParticleVectorLayout Velocity;
    /// <summary>Native mass; used as the gravity acceleration scalar.</summary>
    [FieldOffset(0x24)] public readonly float Mass;
    [FieldOffset(0x28)] public readonly float Size;
    /// <summary>Native energy; 255-based fade/liveness counter.</summary>
    [FieldOffset(0x2C)] public readonly float Energy;
    [FieldOffset(0x30)] public readonly float Phi;
    [FieldOffset(0x34)] public readonly uint Color;
    [FieldOffset(0x38)] public readonly float Moment;
    [FieldOffset(0x3C)] public readonly byte Frame;
    /// <summary>Parameter-block index, distinct from the atlas frame.</summary>
    [FieldOffset(0x3D)] public readonly byte ParameterSetId;
}
