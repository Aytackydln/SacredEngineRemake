using System.Runtime.InteropServices;

namespace Sacred.Particles;

/// <summary>Native advanceTime changes the size of new births using an independently rounded,
/// clamped accumulator. Existing particles retain their size and ordinary motion rates.</summary>
public sealed record SacredParticleBirthSizeRamp(float InitialFactor, float Rate, float MaximumFactor,
    float SizeMultiplier, uint NativeAddress)
{
    public float Advance(float factor, float step) => Math.Min(MaximumFactor, (float)(factor + (double)step * Rate));

    public SacredParticleParameterSet Apply(SacredParticleParameterSet parameters, float factor)
    {
        var emission = parameters.Emission;
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref emission, 1));
        BitConverter.TryWriteBytes(bytes.Slice(0x08, 4), factor * SizeMultiplier);
        return parameters with { Emission = emission };
    }
}
