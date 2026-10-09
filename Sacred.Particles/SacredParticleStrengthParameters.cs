using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public static class SacredParticleStrengthParameters
{
    public static SacredParticleDefinition Resolve(SacredParticleDefinition definition, uint input)
    {
        if (definition.Strength is not { } strength) return definition;
        // The native initializer stores scaled strength before using it in both expressions.
        var scaled = (float)(input * (double)strength.InputScale);
        var size = (float)((scaled + (double)strength.SizeAddend) * strength.SizeMultiplier);
        var growth = (float)(scaled * (double)strength.GrowthMultiplier + strength.GrowthAddend);
        return definition with
        {
            ParameterSets = definition.ParameterSets.Select(set => set with
            {
                Emission = Replace(set.Emission, nameof(SacredParticleEmissionLayout.Size), size),
                Motion = Replace(set.Motion, nameof(SacredParticleMotionLayout.SizeChangeRate), growth)
            }).ToArray()
        };
    }

    private static T Replace<T>(T value, string field, float replacement) where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[(int)Marshal.OffsetOf<T>(field)..], replacement);
        return MemoryMarshal.Read<T>(bytes);
    }
}
