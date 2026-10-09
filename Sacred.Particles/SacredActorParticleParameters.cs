using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Applies the Items.pak input read by Generic subtype 6 at Gold 0x7972B0..0x797302.</summary>
public static class SacredActorParticleParameters
{
    public static SacredParticleDefinition Resolve(SacredParticleDefinition definition, uint authoredBlockRadius)
    {
        if (!definition.UsesActorBlockRadius) return definition;
        // Gold TypeManager::getBlockRadius (0x428CE0) uses 50 for a valid item's zero field.
        var radius = authoredBlockRadius == 0 ? 50d : authoredBlockRadius;
        return definition with
        {
            UsesActorBlockRadius = false,
            ParameterSets = definition.ParameterSets.Select(parameters => parameters with
            {
                Emission = Scale(Scale(parameters.Emission, nameof(SacredParticleEmissionLayout.Size),
                        (float)(parameters.Emission.Size * radius)), nameof(SacredParticleEmissionLayout.Gravity),
                    (float)(parameters.Emission.Gravity * radius)),
                Motion = Scale(parameters.Motion, nameof(SacredParticleMotionLayout.SizeChangeRate),
                    (float)(parameters.Motion.SizeChangeRate * radius))
            }).ToArray()
        };
    }

    private static T Scale<T>(T value, string field, float scaled) where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[(int)Marshal.OffsetOf<T>(field)..], scaled);
        return MemoryMarshal.Read<T>(bytes);
    }
}
