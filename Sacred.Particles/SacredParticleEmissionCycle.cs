using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native countdown controller: movement uses the previous phase; births use
/// the new phase. A negative countdown resets its timer without changing parameters.</summary>
public sealed record SacredParticleEmissionCycle(float InitialDelayMinimum, float InitialDelayWidth,
    float RepeatDelayMinimum, float RepeatDelayWidth, float RandomScale, float BurstThreshold,
    float QuietInterval, float QuietVerticalVelocity, float QuietFadeRate,
    float BurstInterval, float BurstVerticalVelocity, float BurstFadeRate, uint NativeAddress)
{
    public SacredParticleParameterSet Apply(SacredParticleParameterSet parameters, bool burst)
    {
        Span<byte> emission = stackalloc byte[SacredParticleEmissionLayout.SerializedSize];
        Span<byte> motion = stackalloc byte[SacredParticleMotionLayout.SerializedSize];
        var originalEmission = parameters.Emission; var originalMotion = parameters.Motion;
        MemoryMarshal.Write(emission, in originalEmission); MemoryMarshal.Write(motion, in originalMotion);
        BinaryPrimitives.WriteSingleLittleEndian(emission[0x28..], burst ? BurstVerticalVelocity : QuietVerticalVelocity);
        BinaryPrimitives.WriteSingleLittleEndian(emission[0x58..], burst ? BurstInterval : QuietInterval);
        BinaryPrimitives.WriteSingleLittleEndian(motion[0x04..], burst ? BurstFadeRate : QuietFadeRate);
        return parameters with { Emission = MemoryMarshal.Read<SacredParticleEmissionLayout>(emission),
            Motion = MemoryMarshal.Read<SacredParticleMotionLayout>(motion) };
    }
}
