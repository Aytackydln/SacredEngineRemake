using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Native 0x20-byte motion block consumed by 0x7640C0 and serialized by
/// the smoke system. Rates use the native simulation time unit.</summary>
/// <remarks>demo: PS_STD_MOVEMENT; deltaMass, deltaEnergy, deltaSize,
/// deltaPhi, extraForce, extraPotential, respectively.</remarks>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredParticleMotionLayout
{
    public const int SerializedSize = 0x20;
    [FieldOffset(0x00)] public readonly float GravityChangeRate;
    /// <summary>Change of the particle's 255-based fade counter; a nonpositive counter kills it.</summary>
    [FieldOffset(0x04)] public readonly float FadeChangeRate;
    /// <summary>A nonpositive resulting size also kills the particle.</summary>
    [FieldOffset(0x08)] public readonly float SizeChangeRate;
    [FieldOffset(0x0C)] public readonly float AdditionalAngularVelocity;
    [FieldOffset(0x10)] public readonly SacredParticleVectorLayout GravityDirection;
    /// <summary>Nonzero values accelerate along the normalized displacement;
    /// positive values pull toward the emitter, negative values push away.</summary>
    [FieldOffset(0x1C)] public readonly float InwardAcceleration;
}
