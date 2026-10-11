using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed record SacredBloodFountainFxDefinition
{
    public SacredBloodFountainFxStateLayout InitialState { get; init; }
    public float FadeStart { get; init; }
    public float FadeRate { get; init; }
    public float JetSpeed { get; init; }

    internal SacredParticleEmissionLayout WithVelocity(SacredParticleEmissionLayout emission, float speed)
    {
        Span<byte> bytes = stackalloc byte[SacredParticleEmissionLayout.SerializedSize];
        MemoryMarshal.Write(bytes, in emission);
        // The updater authors local X, then applies base orientation (identity in standalone playback).
        BinaryPrimitives.WriteSingleLittleEndian(bytes[0x20..], speed);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[0x24..], 0);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[0x28..], 0);
        return MemoryMarshal.Read<SacredParticleEmissionLayout>(bytes);
    }
}
